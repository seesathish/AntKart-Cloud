namespace AK.Payments.Domain.Enums;

public enum PaymentStatus
{
    Pending = 1,
    Initiated = 2,
    Succeeded = 3,
    Failed = 4,
    Refunded = 5
}
