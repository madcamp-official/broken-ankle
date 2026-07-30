namespace Ashburn.Cutscenes
{
    /// <summary>One caption in a story beat.</summary>
    public readonly struct DialogueLine
    {
        public readonly string Speaker;
        public readonly string Text;

        public DialogueLine(string speaker, string text)
        {
            Speaker = speaker;
            Text = text;
        }
    }
}
