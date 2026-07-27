namespace Morrow.Player
{
    /// <summary>
    /// Something that reads one of the input asset's action maps and can be pointed at a
    /// different one after it has already started.
    ///
    /// Needed because a spawned character does not know which map it wants until it exists:
    /// two players sharing a keyboard need different maps, and Instantiate runs Awake before
    /// the spawner gets a chance to say which.
    /// </summary>
    public interface IUsesActionMap
    {
        void UseActionMap(string actionMap);
    }
}
