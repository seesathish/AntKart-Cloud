namespace AK.Notification.Core.Channels;

// The delivery channels a notification can go out on. Email is implemented now; WhatsApp and Sms
// are deliberate placeholders for later.
//
// THIS ENUM IS THE EXTENSIBILITY SEAM. Adding a channel is an OPEN/CLOSED change:
//   1. add a value here,
//   2. implement INotificationChannel for it (set ChannelType to the new value),
//   3. register that implementation in AddNotificationCore.
// No dispatcher, template, or caller code changes — the dispatcher resolves channels by this type.
public enum NotificationChannelType
{
    Email = 1,
    WhatsApp = 2,
    Sms = 3
}
