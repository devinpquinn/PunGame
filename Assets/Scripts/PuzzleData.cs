using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Puzzle", menuName = "PunGame/Puzzle")]
public class PuzzleData : ScriptableObject
{
    [Tooltip("The puzzle sentence the player selects from.")]
    [TextArea] public string sentence;

    [Tooltip("Audio of the full puzzle sentence being spoken.")]
    public AudioClip sentenceClip;

    [Tooltip("The clues (and their answers) for this puzzle.")]
    public List<ClueEntry> clues = new List<ClueEntry>();
}

[System.Serializable]
public class ClueEntry
{
    [Tooltip("Clue text shown to the player (e.g. \"Soviet canine cosmonaut\").")]
    [TextArea] public string clueText;

    [Tooltip("The answer revealed when solved (e.g. \"Laika\").")]
    public string answerText;

    [Tooltip("Accepted character selections that satisfy this clue (e.g. \"Like a\"). Most have one; some allow multiple spellings.")]
    public List<string> acceptedPatterns = new List<string>();

    [Tooltip("Audio played from the sentence audio source when this clue is solved.")]
    public AudioClip answerClip;
}
