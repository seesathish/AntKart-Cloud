using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Domain.Entities;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace AK.Payments.Application.Commands.InitiatePayment;

// Handles the first step of the Razorpay payment flow.
//
// Flow:
//   1. Create a Payment domain entity (status = Pending)
//   2. Call Razorpay to create an "order" — Razorpay returns an order ID
//   3. Assign the Razorpay order ID to our Payment (status → Initiated)
//   4. Publish PaymentInitiatedIntegrationEvent to the message bus
//   5. Save everything to PostgreSQL (atomic)
//   6. Return the Razorpay order ID + public key to the frontend
//
// The frontend then opens the Razorpay payment widget using the returned order ID and key ID.
// After the user pays, the frontend calls the VerifyPayment endpoint with the payment IDs.
public sealed class InitiatePaymentCommandHandler(
    IUnitOfWork uow,
    IRazorpayClient razorpay,
    IPublishEndpoint publisher,
    IConfiguration configuration)
    : IRequestHandler<InitiatePaymentCommand, InitiatePaymentResponse>
{
    public async Task<InitiatePaymentResponse> Handle(InitiatePaymentCommand request, CancellationToken ct)
    {
        var payment = Payment.Create(request.OrderId, request.UserId, request.CustomerEmail, request.CustomerName, request.OrderNumber, request.Amount, request.Method, request.SavedCardToken);
        await uow.Payments.AddAsync(payment, ct);

        // Amount must be in the smallest currency unit (paise for INR: ₹100 = 10000 paise).
        // The RazorpayGatewayClient handles the × 100 conversion internally.
        var rzpOrder = await razorpay.CreateOrderAsync(request.Amount, "INR", payment.Id.ToString(), ct);
        payment.AssignRazorpayOrder(rzpOrder.Id);

        var evt = new PaymentInitiatedIntegrationEvent(
            payment.Id, payment.OrderId, payment.UserId, payment.Amount, payment.Currency, rzpOrder.Id);
        await publisher.Publish(evt, ct);

        await uow.SaveChangesAsync(ct);
        payment.ClearDomainEvents();

        // Return the Razorpay public key ID to the frontend — it needs this to initialise the payment widget.
        // KeyId is safe to expose (it's a public identifier, not the secret).
        var keyId = configuration["Razorpay:KeyId"] ?? string.Empty;
        return new InitiatePaymentResponse(payment.Id, rzpOrder.Id, keyId, payment.Amount, payment.Currency);
    }
}
