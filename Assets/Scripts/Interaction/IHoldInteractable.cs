namespace Ashburn.Interaction
{
    /// <summary>
    /// An interactable that wants the key held down rather than tapped.
    ///
    /// A separate interface rather than a field on every interactable, and rather than a Hold on
    /// the input action itself: the action is shared by doors, keys and breakers, and putting a
    /// hold on it would make every one of them take two seconds. The few things worth waiting for
    /// say so themselves, and <see cref="PlayerInteractor"/> times them.
    /// </summary>
    public interface IHoldInteractable : IInteractable
    {
        /// <summary>
        /// Seconds the key has to be held before <see cref="IInteractable.Interact"/> runs. Zero or
        /// less behaves exactly like an ordinary press.
        /// </summary>
        float HoldSeconds { get; }
    }
}
