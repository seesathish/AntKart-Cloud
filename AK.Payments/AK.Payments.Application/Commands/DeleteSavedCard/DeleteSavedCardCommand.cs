using MediatR;

namespace AK.Payments.Application.Commands.DeleteSavedCard;

public sealed record DeleteSavedCardCommand(Guid CardId, string UserId) : IRequest;
