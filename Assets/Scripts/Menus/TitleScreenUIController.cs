using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class TitleScreenUIController : MonoBehaviour
{
    private UIDocument document;
    private VisualElement root;
    private VisualElement settingsModal;
    private VisualElement controlsModal;
    private VisualElement creditsModal;
    private VisualElement activeModal;
    private SettingsPanelBinder settings;
    private Button playButton;
    private Button charactersButton;
    private Button settingsButton;
    private Button controlsButton;
    private Button creditsButton;
    private Button quitButton;
    private Button settingsCloseButton;
    private Button controlsCloseButton;
    private Button creditsCloseButton;
    private Button modalReturnButton;
    private Button[] clickSoundButtons;
    private bool callbacksRegistered;

    private void OnEnable()
    {
        MatchOptions.SetCurrent(MatchOptions.Default);

        document = GetComponent<UIDocument>();
        root = document != null ? document.rootVisualElement : null;
        if (root == null)
        {
            Debug.LogError("[TitleScreenUIController] UIDocument has no visual tree.");
            return;
        }

        CacheElements();
        ConsoleUiNavigation.ConfigureButtons(root);
        RegisterCallbacks();
        settings.ShowStoredSettings();
        CloseModal(false);
        root.schedule.Execute(() => playButton?.Focus());
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
        GameSettings.Flush();
        activeModal = null;
        modalReturnButton = null;
    }

    private void CacheElements()
    {
        settingsModal = RequireElement<VisualElement>("settings-modal");
        controlsModal = RequireElement<VisualElement>("controls-modal");
        creditsModal = RequireElement<VisualElement>("credits-modal");
        settings = new SettingsPanelBinder(settingsModal, nameof(TitleScreenUIController));
        playButton = RequireElement<Button>("play-button");
        charactersButton = RequireElement<Button>("characters-button");
        settingsButton = RequireElement<Button>("settings-button");
        controlsButton = RequireElement<Button>("controls-button");
        creditsButton = RequireElement<Button>("credits-button");
        quitButton = RequireElement<Button>("quit-button");
        settingsCloseButton = RequireElement<Button>("settings-close-button");
        controlsCloseButton = RequireElement<Button>("controls-close-button");
        creditsCloseButton = RequireElement<Button>("credits-close-button");
        clickSoundButtons = new[]
        {
            playButton,
            charactersButton,
            settingsButton,
            controlsButton,
            creditsButton,
            quitButton,
            settingsCloseButton,
            controlsCloseButton,
            creditsCloseButton,
        };
    }

    private T RequireElement<T>(string elementName)
        where T : VisualElement
    {
        T element = root.Q<T>(elementName);
        if (element == null)
        {
            Debug.LogError(
                $"[TitleScreenUIController] Missing required {typeof(T).Name} '{elementName}'."
            );
        }
        return element;
    }

    private void RegisterCallbacks()
    {
        if (callbacksRegistered)
            return;

        foreach (Button button in clickSoundButtons)
        {
            if (button != null)
                button.clicked += PlayClick;
        }

        settings.Bind();

        if (playButton != null)
            playButton.clicked += StartGame;
        if (charactersButton != null)
            charactersButton.clicked += OpenCharacters;
        if (settingsButton != null)
            settingsButton.clicked += OpenSettings;
        if (controlsButton != null)
            controlsButton.clicked += OpenControls;
        if (creditsButton != null)
            creditsButton.clicked += OpenCredits;
        if (quitButton != null)
            quitButton.clicked += QuitGame;
        if (settingsCloseButton != null)
            settingsCloseButton.clicked += CloseModalFromButton;
        if (controlsCloseButton != null)
            controlsCloseButton.clicked += CloseModalFromButton;
        if (creditsCloseButton != null)
            creditsCloseButton.clicked += CloseModalFromButton;

        root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        callbacksRegistered = true;
    }

    private void UnregisterCallbacks()
    {
        if (!callbacksRegistered)
            return;

        foreach (Button button in clickSoundButtons)
        {
            if (button != null)
                button.clicked -= PlayClick;
        }

        settings.Unbind();

        if (playButton != null)
            playButton.clicked -= StartGame;
        if (charactersButton != null)
            charactersButton.clicked -= OpenCharacters;
        if (settingsButton != null)
            settingsButton.clicked -= OpenSettings;
        if (controlsButton != null)
            controlsButton.clicked -= OpenControls;
        if (creditsButton != null)
            creditsButton.clicked -= OpenCredits;
        if (quitButton != null)
            quitButton.clicked -= QuitGame;
        if (settingsCloseButton != null)
            settingsCloseButton.clicked -= CloseModalFromButton;
        if (controlsCloseButton != null)
            controlsCloseButton.clicked -= CloseModalFromButton;
        if (creditsCloseButton != null)
            creditsCloseButton.clicked -= CloseModalFromButton;

        root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        callbacksRegistered = false;
    }

    private void PlayClick()
    {
        AudioManager.Instance?.PlayButtonClick();
    }

    private void StartGame()
    {
        SceneManager.LoadScene("JoinGame");
    }

    private void OpenCharacters()
    {
        SceneManager.LoadScene("Characters");
    }

    private void OpenSettings()
    {
        settings.ShowStoredSettings();
        ShowModal(settingsModal, settings.FirstControl, settingsButton);
    }

    private void OpenControls()
    {
        ShowModal(controlsModal, controlsCloseButton, controlsButton);
    }

    private void OpenCredits()
    {
        ShowModal(creditsModal, creditsCloseButton, creditsButton);
    }

    private void ShowModal(VisualElement modal, VisualElement initialFocus, Button returnButton)
    {
        if (modal == null)
            return;

        CloseModal(false);
        activeModal = modal;
        modalReturnButton = returnButton;
        activeModal.RemoveFromClassList("hidden");
        activeModal.BringToFront();
        root.schedule.Execute(() => initialFocus?.Focus());
    }

    private void CloseModalFromButton()
    {
        CloseModal(true);
    }

    private void CloseModal(bool restoreFocus)
    {
        // Volume writes stream in while a slider is dragged, so the disk write waits for the exit.
        if (settingsModal != null && activeModal == settingsModal)
            GameSettings.Flush();

        settingsModal?.AddToClassList("hidden");
        controlsModal?.AddToClassList("hidden");
        creditsModal?.AddToClassList("hidden");

        Button returnButton = modalReturnButton;
        activeModal = null;
        modalReturnButton = null;
        if (restoreFocus)
            root?.schedule.Execute(() => returnButton?.Focus());
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Escape || activeModal == null)
            return;

        CloseModal(true);
        evt.StopImmediatePropagation();
    }

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        float width = evt.newRect.width;
        float height = evt.newRect.height;
        root.EnableInClassList("compact", width < 1500f);
        root.EnableInClassList("narrow", width < 1000f);
        root.EnableInClassList("phone", width < 680f);
        root.EnableInClassList("short", height < 800f);
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        Application.Quit();
    }
}
