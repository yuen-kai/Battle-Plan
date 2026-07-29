using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Binds one settings panel — the three volume rows and the quality row — to GameSettings. The title
/// screen and the in-match overlay share the markup and the styles, so they share the wiring too
/// rather than keeping two copies of it that drift apart.
/// </summary>
public sealed class SettingsPanelBinder
{
    private const float SfxPreviewInterval = 0.14f;

    private readonly string owner;
    private readonly Slider masterVolumeSlider;
    private readonly Slider musicVolumeSlider;
    private readonly Slider sfxVolumeSlider;
    private readonly Label masterVolumeValue;
    private readonly Label musicVolumeValue;
    private readonly Label sfxVolumeValue;
    private readonly DropdownField qualityDropdown;
    private float nextSfxPreviewTime;
    private bool bound;

    public SettingsPanelBinder(VisualElement panel, string owner)
    {
        this.owner = owner;
        masterVolumeSlider = Require<Slider>(panel, "master-volume-slider");
        musicVolumeSlider = Require<Slider>(panel, "music-volume-slider");
        sfxVolumeSlider = Require<Slider>(panel, "sfx-volume-slider");
        masterVolumeValue = Require<Label>(panel, "master-volume-value");
        musicVolumeValue = Require<Label>(panel, "music-volume-value");
        sfxVolumeValue = Require<Label>(panel, "sfx-volume-value");
        qualityDropdown = Require<DropdownField>(panel, "quality-dropdown");
    }

    /// <summary>Where focus belongs when the panel opens: the first row the player can change.</summary>
    public VisualElement FirstControl => masterVolumeSlider;

    public void Bind()
    {
        if (bound)
            return;

        bound = true;
        if (masterVolumeSlider != null)
            masterVolumeSlider.RegisterValueChangedCallback(OnMasterVolumeChanged);
        if (musicVolumeSlider != null)
            musicVolumeSlider.RegisterValueChangedCallback(OnMusicVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.RegisterValueChangedCallback(OnSfxVolumeChanged);
    }

    public void Unbind()
    {
        if (!bound)
            return;

        bound = false;
        if (masterVolumeSlider != null)
            masterVolumeSlider.UnregisterValueChangedCallback(OnMasterVolumeChanged);
        if (musicVolumeSlider != null)
            musicVolumeSlider.UnregisterValueChangedCallback(OnMusicVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.UnregisterValueChangedCallback(OnSfxVolumeChanged);
        if (qualityDropdown != null)
            qualityDropdown.UnregisterValueChangedCallback(OnQualityChanged);
    }

    /// <summary>
    /// Shows what is stored without reporting any of it back as a change, so opening the panel never
    /// counts as an edit and never re-writes what is already saved.
    /// </summary>
    public void ShowStoredSettings()
    {
        GameSettings settings = GameSettings.Current;
        ShowVolume(masterVolumeSlider, masterVolumeValue, settings.masterVolume);
        ShowVolume(musicVolumeSlider, musicVolumeValue, settings.musicVolume);
        ShowVolume(sfxVolumeSlider, sfxVolumeValue, settings.sfxVolume);
        ConfigureQualityDropdown();
    }

    private void ConfigureQualityDropdown()
    {
        if (qualityDropdown == null)
            return;

        qualityDropdown.UnregisterValueChangedCallback(OnQualityChanged);
        qualityDropdown.choices = new List<string>(QualitySettings.names);
        if (qualityDropdown.choices.Count == 0)
        {
            qualityDropdown.SetEnabled(false);
            return;
        }

        int qualityIndex = Mathf.Clamp(
            GameSettings.Current.ResolvedQualityLevel,
            0,
            qualityDropdown.choices.Count - 1
        );
        qualityDropdown.SetValueWithoutNotify(qualityDropdown.choices[qualityIndex]);
        qualityDropdown.RegisterValueChangedCallback(OnQualityChanged);
    }

    private void OnQualityChanged(ChangeEvent<string> evt)
    {
        int qualityIndex = qualityDropdown?.choices?.IndexOf(evt.newValue) ?? -1;
        if (qualityIndex >= 0)
            GameSettings.SetQualityLevel(qualityIndex);
    }

    private void OnMasterVolumeChanged(ChangeEvent<float> evt)
    {
        GameSettings.SetMasterVolume(evt.newValue);
        ShowVolumeReadout(masterVolumeValue, GameSettings.Current.masterVolume);
    }

    private void OnMusicVolumeChanged(ChangeEvent<float> evt)
    {
        GameSettings.SetMusicVolume(evt.newValue);
        ShowVolumeReadout(musicVolumeValue, GameSettings.Current.musicVolume);
    }

    private void OnSfxVolumeChanged(ChangeEvent<float> evt)
    {
        GameSettings.SetSfxVolume(evt.newValue);
        ShowVolumeReadout(sfxVolumeValue, GameSettings.Current.sfxVolume);
        PreviewSfx();
    }

    private static void ShowVolume(Slider slider, Label readout, float volume)
    {
        slider?.SetValueWithoutNotify(volume);
        ShowVolumeReadout(readout, volume);
    }

    private static void ShowVolumeReadout(Label readout, float volume)
    {
        if (readout != null)
            readout.text = $"{GameSettings.ToPercent(volume)}%";
    }

    /// <summary>
    /// An effects level you cannot hear is not a choice, so dragging the slider plays the click it
    /// governs. Throttled because the slider reports every pointer move.
    /// </summary>
    private void PreviewSfx()
    {
        if (Time.unscaledTime < nextSfxPreviewTime)
            return;

        nextSfxPreviewTime = Time.unscaledTime + SfxPreviewInterval;
        AudioManager.Instance?.PlayButtonClick();
    }

    private T Require<T>(VisualElement panel, string elementName)
        where T : VisualElement
    {
        T element = panel?.Q<T>(elementName);
        if (element == null)
            Debug.LogError($"[{owner}] Missing required {typeof(T).Name} '{elementName}'.");
        return element;
    }
}
