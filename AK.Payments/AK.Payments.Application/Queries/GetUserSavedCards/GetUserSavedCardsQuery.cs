using AK.Payments.Application.DTOs;
using MediatR;

namespace AK.Payments.Application.Queries.GetUserSavedCards;

public sealed record GetUserSavedCardsQuery(string UserId) : IRequest<IReadOnlyList<SavedCardDto>>;
