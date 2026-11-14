using De6.Contracts;

namespace De6.Booking.Domain;

public interface ICommandCoordinator
{
    // Cancellation stops waiting for the reply; it cannot abandon an operation after LocalApplied.
    Task<CommandReply> SubmitAsync(AuthenticatedCommand command, CancellationToken replyCancellation);
}
