namespace Ashburn.Player
{
    /// <summary>
    /// How the player is travelling. Each step up the list is faster and louder, which is the
    /// trade the player is making every time they choose one.
    /// </summary>
    public enum MovementMode
    {
        Crouch,
        Walk,
        Run,
    }
}
