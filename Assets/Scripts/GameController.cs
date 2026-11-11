using System.Collections;
using System.Collections.Generic;
using System.Text;
using Febucci.TextAnimatorForUnity.TextMeshPro;
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
    [Tooltip("Canvas Group on the selection box, faded out when a selection is released. If unset, it is looked up on the selection box.")]
    [SerializeField] CanvasGroup selectionBoxCanvasGroup;
    [Tooltip("Seconds the selection box takes to fade out after the mouse is released. The player cannot start a new selection until the fade completes.")]
    [SerializeField] float selectionBoxFadeDuration = 0.2f;
    [Tooltip("Pixels the mouse must move while held before selection starts.")]
    [SerializeField] float dragThreshold = 8f;
    [Tooltip("Color of the selected text while a correct selection's audio plays.")]
    [SerializeField] Color correctSelectionColor = Color.green;

    [Tooltip("Text Animator on the selected text, used to play an effect on a correct selection. If left unset, it is looked up on the selected text object.")]
    [SerializeField] TextAnimator_TMP selectedTextAnimator;
    [Tooltip("Text Animator effect tag whose one-shot appearance plays on each character as it is selected. Should be an effect with an appearance (e.g. bounce).")]
    [SerializeField] string selectionEffectTag = "bounce";
    [Tooltip("Text Animator effect tag applied to a correct selection while its audio plays.")]
    [SerializeField] string correctSelectionEffectTag = "wave";
    [Tooltip("Text Animator appearance effect tag used to animate the remaining (non-selected) characters back in after a correct selection's audio finishes. Should be an effect with an appearance (e.g. fade).")]
    [SerializeField] string revealBackEffectTag = "fade";
    [Tooltip("Delay between each step as the remaining text spreads outward from the selection. Set to 0 to reveal it all at once.")]
    [SerializeField] float revealBackPerCharDelay = 0.03f;

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
    Color selectedTextDefaultColor;
    Vector3[] charCenters;
    int[] charSourceIndices;
    bool[] charSelected;
    bool[] sourceSelected;
    Vector2 dragStart;
    bool isPressPending;
    bool isSelecting;
    bool wasAudioPlaying;
    bool isResolving;
    bool isFadingSelectionBox;

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
        selectedTextDefaultColor = selectedText.color;

        if (selectedTextAnimator == null && selectedText != null)
            selectedTextAnimator = selectedText.GetComponent<TextAnimator_TMP>();
        // The animator is only enabled while a correct selection plays its effect.
        if (selectedTextAnimator != null) selectedTextAnimator.enabled = false;

        Canvas canvas = sentenceText.canvas;
        uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        if (selectionBox != null)
        {
            selectionBox.anchorMin = selectionBox.anchorMax = selectionBox.pivot = new Vector2(0.5f, 0.5f);
            if (selectionBoxCanvasGroup == null)
                selectionBoxCanvasGroup = selectionBox.GetComponent<CanvasGroup>();
            selectionBox.gameObject.SetActive(false);
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
        if (isResolving || isFadingSelectionBox) return;

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
        ShowSelectionBox();
        PrepareSelectedTextEffect();
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

    // Loads the whole sentence into the selected-text animator wrapped in the one-shot
    // appearance effect ({tag}...{/}), with every character hidden. Characters are then
    // revealed one-by-one in UpdateSelection, which plays the appearance as each is selected.
    void PrepareSelectedTextEffect()
    {
        if (selectedTextAnimator == null) return;

        selectedTextAnimator.enabled = true;
        selectedTextAnimator.SetAppearancesActive(true);
        selectedTextAnimator.SetText(WrapAppearance(sentence, selectionEffectTag), hideText: true);

        // Keep the whole text within the visible range; the per-character visible flags
        // (toggled in UpdateSelection) decide what actually shows and play the appearance.
        selectedTextAnimator.FirstVisibleCharacter = 0;
        selectedTextAnimator.MaxVisibleCharacters = sentence.Length;
        for (int i = 0; i < sentence.Length; i++)
            selectedTextAnimator.SetVisibilityChar(i, false, canPlayEffects: false);
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
                {
                    sourceSelected[src] = inside;
                    // Reveal a newly selected character (playing its appearance effect);
                    // hide a deselected one instantly.
                    if (selectedTextAnimator != null)
                        selectedTextAnimator.SetVisibilityChar(src, inside, canPlayEffects: inside);
                }
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
            FadeOutSelectionBox();
            selectedText.color = correctSelectionColor;
            ApplyCorrectSelectionEffect();

            if (PlayClip(clueRuntimes[clueIndex].data.answerClip))
                pendingSolveIndex = clueIndex;
            else
                ResolveClue(clueIndex);
        }
        else
        {
            SetSelecting(false);
            FadeOutSelectionBox();
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

        // Animate the remaining (non-selected) characters back in before returning to the
        // plain sentence view. The coroutine also eases the highlight color back to default.
        isResolving = true;
        StartCoroutine(AnimateRemainingTextIn(FinishResolve));
    }

    // Reveals the characters that were not part of the selection, playing the reveal
    // appearance effect on each (optionally staggered left-to-right), then waits for the
    // animation to finish. Already-selected characters stay visible without re-animating.
    IEnumerator AnimateRemainingTextIn(System.Action onComplete)
    {
        if (selectedTextAnimator == null)
        {
            selectedText.color = selectedTextDefaultColor;
            onComplete?.Invoke();
            yield break;
        }

        selectedTextAnimator.enabled = true;
        selectedTextAnimator.SetAppearancesActive(true);
        // Load the whole sentence (no masking) wrapped in the reveal appearance effect,
        // hidden, so appearances are available to play as characters are revealed.
        selectedTextAnimator.SetText(WrapAppearance(sentence, revealBackEffectTag), hideText: true);
        selectedTextAnimator.FirstVisibleCharacter = 0;
        selectedTextAnimator.MaxVisibleCharacters = sentence.Length;

        // Keep the already-selected characters shown without re-animating them.
        for (int i = 0; i < sentence.Length; i++)
            selectedTextAnimator.SetVisibilityChar(i, sourceSelected[i], canPlayEffects: false);

        // Reveal the remaining characters spreading outward from the selection: group them
        // by their distance to the nearest selected character and reveal each ring (both
        // sides at once), pausing between rings so the effect radiates from the selection.
        // As each character is revealed in the selected text, its counterpart is hidden in
        // the deemphasized text, mirroring the swap done during selection.
        int n = sentence.Length;
        int[] distance = new int[n];
        int maxDistance = ComputeDistanceToSelection(distance);
        bool[] revealedBack = new bool[n];

        // Ease the selected segment's color from its highlight back to default over the
        // whole spread. Start from the highlight color explicitly in case it drifted.
        selectedText.color = correctSelectionColor;
        float spreadDuration = maxDistance * Mathf.Max(0f, revealBackPerCharDelay);
        float elapsed = 0f;

        for (int d = 1; d <= maxDistance; d++)
        {
            bool revealedAny = false;
            for (int i = 0; i < n; i++)
            {
                if (sourceSelected[i] || distance[i] != d) continue;
                selectedTextAnimator.SetVisibilityChar(i, true, canPlayEffects: true);
                revealedBack[i] = true;
                revealedAny = true;
            }
            if (!revealedAny) continue;

            deemphasizedText.text = BuildMaskedSentence(hideSelected: true, alsoHide: revealedBack);

            // Advance one spread step, easing the color toward default as time passes.
            float stepEnd = elapsed + revealBackPerCharDelay;
            while (elapsed < stepEnd)
            {
                elapsed += Time.deltaTime;
                float t = spreadDuration > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / spreadDuration)) : 1f;
                selectedText.color = Color.Lerp(correctSelectionColor, selectedTextDefaultColor, t);
                yield return null;
            }
        }

        selectedText.color = selectedTextDefaultColor;

        // Wait for every appearance effect to finish before tearing down.
        while (!selectedTextAnimator.allLettersShown)
            yield return null;

        onComplete?.Invoke();
    }

    void FinishResolve()
    {
        isResolving = false;
        ClearCorrectSelectionEffect();
        SetSelecting(false);
    }

    // Fills 'distance' with each character's index distance to the nearest selected
    // character (0 for selected characters) and returns the largest distance found. If
    // nothing is selected, falls back to a left-to-right order.
    int ComputeDistanceToSelection(int[] distance)
    {
        int n = distance.Length;
        const int Far = int.MaxValue / 2;

        // Forward pass: distance to the nearest selected character on the left.
        int last = -1;
        for (int i = 0; i < n; i++)
        {
            if (sourceSelected[i]) { last = i; distance[i] = 0; }
            else distance[i] = last < 0 ? Far : i - last;
        }

        // Backward pass: fold in the nearest selected character on the right.
        last = -1;
        bool hasSelection = false;
        for (int i = n - 1; i >= 0; i--)
        {
            if (sourceSelected[i]) { last = i; distance[i] = 0; hasSelection = true; }
            else if (last >= 0) distance[i] = Mathf.Min(distance[i], last - i);
        }

        if (!hasSelection)
        {
            for (int i = 0; i < n; i++) distance[i] = i + 1;
            return n;
        }

        int maxDistance = 0;
        for (int i = 0; i < n; i++)
            if (!sourceSelected[i] && distance[i] > maxDistance) maxDistance = distance[i];
        return maxDistance;
    }

    void ApplyCorrectSelectionEffect()
    {
        if (selectedTextAnimator == null) return;

        selectedTextAnimator.enabled = true;
        // The correct-selection effect (e.g. wave) is a continuous behavior: <tag>...</tag>.
        SetSelectedText(BuildMaskedSentence(hideSelected: false,
            BehaviorOpen(correctSelectionEffectTag), BehaviorClose(correctSelectionEffectTag)));
    }

    void ClearCorrectSelectionEffect()
    {
        if (selectedTextAnimator == null) return;

        selectedTextAnimator.enabled = false;
    }

    void RebuildTexts()
    {
        deemphasizedText.text = BuildMaskedSentence(hideSelected: true);
        // When an animator is present, the selected text is loaded once in
        // PrepareSelectedTextEffect and revealed per-character via SetVisibilityChar.
        if (selectedTextAnimator != null) return;
        selectedText.text = BuildMaskedSentence(hideSelected: false);
    }

    // Text Animator effect tags: behaviors (continuous) use angle brackets, appearances
    // (one-shot, played when a character is revealed) use curly brackets.
    static string BehaviorOpen(string tag) => string.IsNullOrEmpty(tag) ? null : "<" + tag + ">";
    static string BehaviorClose(string tag) => string.IsNullOrEmpty(tag) ? null : "</" + tag + ">";
    static string WrapAppearance(string text, string tag)
        => string.IsNullOrEmpty(tag) ? text : "{" + tag + "}" + text + "{/}";

    // The selected text is routed through Text Animator (when present) so it manages
    // character visibility itself; assigning its TMP text directly would leave the
    // characters hidden.
    void SetSelectedText(string text)
    {
        if (selectedTextAnimator != null) selectedTextAnimator.SetText(text);
        else selectedText.text = text;
    }

    string BuildMaskedSentence(bool hideSelected, string effectOpenTag = null, string effectCloseTag = null, bool[] alsoHide = null)
    {
        bool useEffect = !string.IsNullOrEmpty(effectOpenTag);

        var sb = new StringBuilder(sentence.Length + 64);
        bool hiding = false;
        bool emphasizing = false;

        for (int i = 0; i < sentence.Length; i++)
        {
            bool shouldHide = sourceSelected[i] == hideSelected;
            if (alsoHide != null && alsoHide[i]) shouldHide = true;
            // The effect decorates the visible (selected) characters only.
            bool shouldEmphasize = useEffect && !shouldHide;

            // Emit closing tags before opening tags to keep regions properly nested.
            if (hiding && !shouldHide) { sb.Append(HideCloseTag); hiding = false; }
            if (emphasizing && !shouldEmphasize) { sb.Append(effectCloseTag); emphasizing = false; }
            if (!emphasizing && shouldEmphasize) { sb.Append(effectOpenTag); emphasizing = true; }
            if (!hiding && shouldHide) { sb.Append(HideOpenTag); hiding = true; }

            sb.Append(sentence[i]);
        }

        if (emphasizing) sb.Append(effectCloseTag);
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
        // The animator must be enabled while the selected text is shown so it reveals
        // (and later animates) the characters it manages.
        if (selectedTextAnimator != null) selectedTextAnimator.enabled = selecting;
    }

    // Shows the selection box fully opaque, cancelling any in-progress fade-out.
    void ShowSelectionBox()
    {
        if (selectionBox == null) return;
        StopCoroutine(nameof(FadeSelectionBoxRoutine));
        isFadingSelectionBox = false;
        if (selectionBoxCanvasGroup != null) selectionBoxCanvasGroup.alpha = 1f;
        selectionBox.gameObject.SetActive(true);
    }

    // Fades the selection box out, blocking new selections until the fade completes.
    void FadeOutSelectionBox()
    {
        if (selectionBox == null) return;
        StopCoroutine(nameof(FadeSelectionBoxRoutine));
        StartCoroutine(nameof(FadeSelectionBoxRoutine));
    }

    IEnumerator FadeSelectionBoxRoutine()
    {
        isFadingSelectionBox = true;

        if (selectionBoxCanvasGroup != null && selectionBoxFadeDuration > 0f)
        {
            float startAlpha = selectionBoxCanvasGroup.alpha;
            float elapsed = 0f;
            while (elapsed < selectionBoxFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / selectionBoxFadeDuration);
                selectionBoxCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
                yield return null;
            }
            selectionBoxCanvasGroup.alpha = 0f;
        }

        selectionBox.gameObject.SetActive(false);
        if (selectionBoxCanvasGroup != null) selectionBoxCanvasGroup.alpha = 1f;
        isFadingSelectionBox = false;
    }
}
