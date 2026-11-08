using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class GameController : MonoBehaviour
{
    const string HideOpenTag = "<color=#FFFFFF00>";
    const string HideCloseTag = "</color>";

    [SerializeField, TextArea] string sentence = "The quick brown fox jumps over the lazy dog.";
    [SerializeField] TMP_Text sentenceText;
    [SerializeField] TMP_Text deemphasizedText;
    [SerializeField] TMP_Text selectedText;
    [Tooltip("RectTransform whose width is set to the sentence's preferred width when the text is applied.")]
    [SerializeField] RectTransform sentenceWidthTarget;
    [Tooltip("Optional visual for the selection box (e.g. a semi-transparent Image).")]
    [SerializeField] RectTransform selectionBox;
    [Tooltip("Pixels the mouse must move while held before selection starts.")]
    [SerializeField] float dragThreshold = 8f;

    [SerializeField] Button playButton;
    [SerializeField] GameObject buttonImage;
    [SerializeField] GameObject buttonImagePressed;
    [SerializeField] AudioSource sentenceAudioSource;
    [SerializeField] AudioClip sentenceClip;

    [Header("Puzzle")]
    [Tooltip("The puzzle to load. When set, overrides the sentence/clip above.")]
    [SerializeField] PuzzleData puzzle;

    [Header("Clues & Answers")]
    [Tooltip("Parent the spawned clue objects are added under.")]
    [SerializeField] Transform clueParent;
    [Tooltip("Parent the spawned answer objects are added under.")]
    [SerializeField] Transform answerParent;
    [Tooltip("Prefab spawned for each clue. Must contain a TMP_Text.")]
    [SerializeField] GameObject cluePrefab;
    [Tooltip("Prefab spawned for each answer. Must contain a TMP_Text.")]
    [SerializeField] GameObject answerPrefab;

    Camera uiCamera;
    Vector3[] charCenters;
    int[] charSourceIndices;
    bool[] charSelected;
    bool[] sourceSelected;
    Vector2 dragStart;
    bool isPressPending;
    bool isSelecting;
    bool wasAudioPlaying;

    readonly List<ClueRuntime> clueRuntimes = new List<ClueRuntime>();
    int pendingSolveIndex = -1;

    bool IsAudioPlaying => sentenceAudioSource != null && sentenceAudioSource.isPlaying;

    class ClueRuntime
    {
        public ClueEntry data;
        public TMP_Text clueBackground;
        public TMP_Text clueForeground;
        public TMP_Text answerBackground;
        public TMP_Text answerForeground;
        public bool solved;
    }

    void Start()
    {
        if (puzzle != null)
        {
            sentence = puzzle.sentence;
            sentenceClip = puzzle.sentenceClip;
        }

        ApplySentenceText();

        Canvas canvas = sentenceText.canvas;
        uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        if (selectionBox != null)
        {
            selectionBox.anchorMin = selectionBox.anchorMax = selectionBox.pivot = new Vector2(0.5f, 0.5f);
        }

        if (playButton != null) playButton.onClick.AddListener(PlaySentence);

        SpawnClues();
        SetSelecting(false);
    }

    void ApplySentenceText()
    {
        sentenceText.text = sentence;
        deemphasizedText.text = sentence;
        selectedText.text = sentence;

        sentenceText.ForceMeshUpdate();
        if (sentenceWidthTarget != null)
            SetRectWidth(sentenceWidthTarget, sentenceText.preferredWidth);
    }

    static void SetRectWidth(RectTransform rt, float width)
    {
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
    }

    void SpawnClues()
    {
        clueRuntimes.Clear();
        if (puzzle == null) return;

        foreach (ClueEntry entry in puzzle.clues)
        {
            var rt = new ClueRuntime { data = entry };

            if (cluePrefab != null && clueParent != null)
            {
                GameObject go = Instantiate(cluePrefab, clueParent);
                GetTexts(go, out rt.clueBackground, out rt.clueForeground);
                SetText(rt.clueBackground, rt.clueForeground, entry.clueText);
                // An unanswered clue is emphasized.
                SetEmphasis(rt.clueBackground, rt.clueForeground, emphasized: true);
            }

            if (answerPrefab != null && answerParent != null)
            {
                GameObject go = Instantiate(answerPrefab, answerParent);
                GetTexts(go, out rt.answerBackground, out rt.answerForeground);
                SetText(rt.answerBackground, rt.answerForeground, Obfuscate(entry.answerText));
                // An unsolved answer is de-emphasized.
                SetEmphasis(rt.answerBackground, rt.answerForeground, emphasized: false);
            }

            clueRuntimes.Add(rt);
        }
    }

    // The regular (root) object holds the background text; its child holds the foreground text.
    static void GetTexts(GameObject go, out TMP_Text background, out TMP_Text foreground)
    {
        background = go.GetComponent<TMP_Text>();
        foreground = null;
        foreach (TMP_Text t in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t != background)
            {
                foreground = t;
                break;
            }
        }
    }

    static void SetText(TMP_Text background, TMP_Text foreground, string text)
    {
        if (background != null) background.text = text;
        if (foreground != null) foreground.text = text;
    }

    // Emphasized shows the foreground text; de-emphasized shows the background text.
    // The component's enabled flag is toggled (not the GameObject) because the
    // foreground object is a child of the background object.
    static void SetEmphasis(TMP_Text background, TMP_Text foreground, bool emphasized)
    {
        if (background != null) background.enabled = !emphasized;
        if (foreground != null) foreground.enabled = emphasized;
    }

    static string Obfuscate(string answer)
    {
        if (string.IsNullOrEmpty(answer)) return string.Empty;

        var sb = new StringBuilder(answer.Length);
        foreach (char c in answer)
            sb.Append(char.IsWhiteSpace(c) ? c : '?');
        return sb.ToString();
    }

    void PlaySentence()
    {
        AudioClip clip = sentenceClip != null
            ? sentenceClip
            : (sentenceAudioSource != null ? sentenceAudioSource.clip : null);

        if (PlayClip(clip)) SetPlayButtonVisual(pressed: true);
    }

    bool PlayClip(AudioClip clip)
    {
        if (sentenceAudioSource == null || clip == null) return false;

        sentenceAudioSource.clip = clip;
        sentenceAudioSource.Play();
        wasAudioPlaying = true;
        if (playButton != null) playButton.interactable = false;
        return true;
    }

    void SetPlayButtonVisual(bool pressed)
    {
        if (buttonImage != null) buttonImage.SetActive(!pressed);
        if (buttonImagePressed != null) buttonImagePressed.SetActive(pressed);
    }

    void Update()
    {
        if (wasAudioPlaying && !IsAudioPlaying)
        {
            wasAudioPlaying = false;
            if (playButton != null) playButton.interactable = true;
            SetPlayButtonVisual(pressed: false);

            if (pendingSolveIndex >= 0)
            {
                int index = pendingSolveIndex;
                pendingSolveIndex = -1;
                ResolveClue(index);
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 mousePos = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (IsAudioPlaying) return;
            dragStart = mousePos;
            isPressPending = true;
        }
        else if (isPressPending)
        {
            if (!mouse.leftButton.isPressed)
                isPressPending = false;
            else if (Vector2.Distance(dragStart, mousePos) >= dragThreshold)
            {
                isPressPending = false;
                BeginSelection(mousePos);
            }
        }
        else if (isSelecting && mouse.leftButton.isPressed)
            UpdateSelection(mousePos);
        else if (isSelecting)
            EndSelection();
    }

    void BeginSelection(Vector2 screenPos)
    {
        sourceSelected = new bool[sentence.Length];
        SetSelecting(true);
        RebuildTexts();

        // Text has no tags yet, so character indices map directly to the sentence.
        deemphasizedText.ForceMeshUpdate();
        TMP_TextInfo info = deemphasizedText.textInfo;
        int count = info.characterCount;

        charCenters = new Vector3[count];
        charSourceIndices = new int[count];
        charSelected = new bool[count];

        for (int i = 0; i < count; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            charCenters[i] = (ci.bottomLeft + ci.topRight) * 0.5f;
            charSourceIndices[i] = ci.index;
        }

        UpdateSelection(screenPos);
    }

    void UpdateSelection(Vector2 screenPos)
    {
        Rect rect = Rect.MinMaxRect(
            Mathf.Min(dragStart.x, screenPos.x), Mathf.Min(dragStart.y, screenPos.y),
            Mathf.Max(dragStart.x, screenPos.x), Mathf.Max(dragStart.y, screenPos.y));

        bool changed = false;
        Transform textTransform = deemphasizedText.transform;

        for (int i = 0; i < charCenters.Length; i++)
        {
            Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(uiCamera, textTransform.TransformPoint(charCenters[i]));
            bool inside = rect.Contains(screenCenter);
            if (inside != charSelected[i])
            {
                charSelected[i] = inside;
                int src = charSourceIndices[i];
                if (src >= 0 && src < sourceSelected.Length)
                    sourceSelected[src] = inside;
                changed = true;
            }
        }

        if (changed) RebuildTexts();
        UpdateSelectionBoxVisual(screenPos);
    }

    void EndSelection()
    {
        string selected = BuildSelectedString();
        int clueIndex = FindMatchingClue(selected);

        if (clueIndex >= 0)
        {
            // Correct answer: keep the selection visible and resolve once the clip finishes.
            isSelecting = false;
            if (selectionBox != null) selectionBox.gameObject.SetActive(false);

            if (PlayClip(clueRuntimes[clueIndex].data.answerClip))
                pendingSolveIndex = clueIndex;
            else
                ResolveClue(clueIndex);
        }
        else
        {
            SetSelecting(false);
        }
    }

    string BuildSelectedString()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < sentence.Length; i++)
        {
            if (sourceSelected[i]) sb.Append(sentence[i]);
        }
        return sb.ToString().Trim();
    }

    int FindMatchingClue(string selection)
    {
        if (string.IsNullOrEmpty(selection)) return -1;

        for (int i = 0; i < clueRuntimes.Count; i++)
        {
            ClueRuntime rt = clueRuntimes[i];
            if (rt.solved) continue;

            foreach (string pattern in rt.data.acceptedPatterns)
            {
                if (selection == pattern) return i;
            }
        }
        return -1;
    }

    void ResolveClue(int index)
    {
        ClueRuntime rt = clueRuntimes[index];
        rt.solved = true;

        // A solved clue is de-emphasized.
        SetEmphasis(rt.clueBackground, rt.clueForeground, emphasized: false);

        // A solved answer is emphasized and reveals its real text.
        SetText(rt.answerBackground, rt.answerForeground, rt.data.answerText);
        SetEmphasis(rt.answerBackground, rt.answerForeground, emphasized: true);

        SetSelecting(false);
    }

    void RebuildTexts()
    {
        deemphasizedText.text = BuildMaskedSentence(hideSelected: true);
        selectedText.text = BuildMaskedSentence(hideSelected: false);
    }

    string BuildMaskedSentence(bool hideSelected)
    {
        var sb = new StringBuilder(sentence.Length + 64);
        bool hiding = false;

        for (int i = 0; i < sentence.Length; i++)
        {
            bool shouldHide = sourceSelected[i] == hideSelected;
            if (shouldHide != hiding)
            {
                sb.Append(shouldHide ? HideOpenTag : HideCloseTag);
                hiding = shouldHide;
            }
            sb.Append(sentence[i]);
        }

        if (hiding) sb.Append(HideCloseTag);
        return sb.ToString();
    }

    void UpdateSelectionBoxVisual(Vector2 screenPos)
    {
        if (selectionBox == null) return;

        var parent = (RectTransform)selectionBox.parent;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, dragStart, uiCamera, out Vector2 a);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, uiCamera, out Vector2 b);

        selectionBox.localPosition = (a + b) * 0.5f;
        selectionBox.sizeDelta = new Vector2(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }

    void SetSelecting(bool selecting)
    {
        isSelecting = selecting;
        sentenceText.gameObject.SetActive(!selecting);
        deemphasizedText.gameObject.SetActive(selecting);
        selectedText.gameObject.SetActive(selecting);
        if (selectionBox != null) selectionBox.gameObject.SetActive(selecting);
    }
}
