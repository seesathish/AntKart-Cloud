using AK.BuildingBlocks.Messaging.EventGrid;
using AK.BuildingBlocks.Messaging.IntegrationEvents;
using AK.BuildingBlocks.Messaging.Notifications;
using AK.Payments.Application.Common.Interfaces;
using AK.Payments.Application.DTOs;
using AK.Payments.Application.Mapping;
using MassTransit;
using MediatR;

namespace AK.Payments.Application.Commands.VerifyPayment;

// Handles the second (final) step of the Razorpay payment flow.
//
// After the user completes payment in the Razorpay widget, the frontend receives:
//   - razorpay_order_id
//   - razorpay_payment_id
//   - razorpay_signature  (HMAC-SHA256 of "orderId|paymentId" using our key secret)
//
// The frontend posts these to our VerifyPayment endpoint. We verify the signature server-side
// using our secret key — this proves the payment really came from Razorpay, not a spoofed request.
//
// Success → mark payment Succeeded → publish PaymentSucceededIntegrationEvent
//           → AK.Order consumer updates order to Paid
//           → AK.Notification consumer sends payment receipt email
// Failure → mark payment Failed → publish PaymentFailedIntegrationEvent
//           → AK.Order consumer updates order to PaymentFailed
public sealed class VerifyPaymentCommandHandler(
    IUnitOfWork uow,
    IRazorpayClient razorpay,
    IPublishEndpoint publisher,
    IEventGridSideEffectPublisher sideEffects)
    : IRequestHandler<VerifyPaymentCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(VerifyPaymentCommand request, CancellationToken ct)
    {
        var payment = await uow.Payments.GetByIdAsync(request.PaymentId, ct)
            ?? throw new KeyNotFoundException($"Payment {request.PaymentId} not found.");

        // Signature verification: Razorpay computes HMAC-SHA256(orderId + "|" + paymentId)
        // using our key secret. We recompute and compare — any mismatch means the request was tampered.
        var isValid = razorpay.VerifyPaymentSignature(
            request.RazorpayOrderId, request.RazorpayPaymentId, request.RazorpaySignature);

        if (isValid)
        {
            payment.MarkSucceeded(request.RazorpayPaymentId, request.RazorpaySignature);
            var succeededEvt = new PaymentSucceededIntegrationEvent(
                payment.Id, payment.OrderId, payment.UserId,
                payment.CustomerEmail, payment.CustomerName, payment.OrderNumber,
                payment.Amount, request.RazorpayPaymentId);
            await publisher.Publish(succeededEvt, ct);
        }
        else
        {
            payment.MarkFailed("Signature verification failed.");
            var failedEvt = new PaymentFailedIntegrationEvent(
                payment.Id, payment.OrderId, payment.UserId,
                payment.CustomerEmail, payment.CustomerName, payment.OrderNumber,
                "Signature verification failed.");
            await publisher.Publish(failedEvt, ct);
        }

        // Durable commit: persists the payment outcome AND the outbox integration event atomically.
        await uow.SaveChangesAsync(ct);
        payment.ClearDomainEvents();

        // COMMIT-THEN-NOTIFY. The payment outcome is now durably committed. Only now do we emit the
        // customer notification as a FIRE-AND-FORGET Event Grid side-effect — strictly AFTER the
        // commit, never inside the transaction. TryPublishAsync NEVER throws, so a notification
        // failure cannot fail this handler or roll back the payment. customerEmail is already a field
        // on the Payment entity (captured at InitiatePayment), so it is available here directly.
        if (isValid)
            await sideEffects.TryPublishAsync(
                NotificationEventTypes.PaymentSucceeded,
                $"payments/{payment.Id}",
                new PaymentSucceededNotification(
                    payment.CustomerEmail, payment.CustomerName, payment.OrderNumber,
                    payment.Amount, payment.Currency, request.RazorpayPaymentId, DateTimeOffset.UtcNow),
                ct);
        else
            await sideEffects.TryPublishAsync(
                NotificationEventTypes.PaymentFailed,
                $"payments/{payment.Id}",
                new PaymentFailedNotification(
                    payment.CustomerEmail, payment.CustomerName, payment.OrderNumber,
                    "Signature verification failed.", DateTimeOffset.UtcNow),
                ct);

        return PaymentMapper.ToDto(payment);
    }
}
