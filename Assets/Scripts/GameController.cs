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
    [Tooltip("Optional visual for the selection box (e.g. a semi-transparent Image).")]
    [SerializeField] RectTransform selectionBox;
    [Tooltip("Pixels the mouse must move while held before selection starts.")]
    [SerializeField] float dragThreshold = 8f;

    [SerializeField] Button playButton;
    [SerializeField] AudioSource sentenceAudioSource;
    [SerializeField] AudioClip sentenceClip;

    Camera uiCamera;
    Vector3[] charCenters;
    int[] charSourceIndices;
    bool[] charSelected;
    bool[] sourceSelected;
    Vector2 dragStart;
    bool isPressPending;
    bool isSelecting;
    bool wasAudioPlaying;

    bool IsAudioPlaying => sentenceAudioSource != null && sentenceAudioSource.isPlaying;

    void Start()
    {
        sentenceText.text = sentence;
        deemphasizedText.text = sentence;
        selectedText.text = sentence;

        Canvas canvas = sentenceText.canvas;
        uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

        if (selectionBox != null)
        {
            selectionBox.anchorMin = selectionBox.anchorMax = selectionBox.pivot = new Vector2(0.5f, 0.5f);
        }

        if (playButton != null) playButton.onClick.AddListener(PlaySentence);

        SetSelecting(false);
    }

    void PlaySentence()
    {
        if (sentenceAudioSource == null) return;

        if (sentenceClip != null) sentenceAudioSource.clip = sentenceClip;
        if (sentenceAudioSource.clip == null) return;

        sentenceAudioSource.Play();
        wasAudioPlaying = true;
        if (playButton != null) playButton.interactable = false;
    }

    void Update()
    {
        if (wasAudioPlaying && !IsAudioPlaying)
        {
            wasAudioPlaying = false;
            if (playButton != null) playButton.interactable = true;
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
        var sb = new StringBuilder();
        for (int i = 0; i < sentence.Length; i++)
        {
            if (sourceSelected[i]) sb.Append(sentence[i]);
        }
        if (sb.Length > 0) Debug.Log(sb.ToString());

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
