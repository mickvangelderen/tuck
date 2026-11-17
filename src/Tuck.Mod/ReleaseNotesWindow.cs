using System;
using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Tuck.Mod;

internal sealed class ReleaseNotesWindow : MonoBehaviour
{
    private const string VersionFile = "acknowledged-release.txt";
    private static bool _attempted;
    private ModManifest _manifest = null!;
    private Options _options = null!;
    private MainMenuUIController _menu = null!;
    private CustomUIWindow? _window;

    internal static ReleaseNotesWindow? Attach(MainMenuUIController menu, ModManifest manifest, Options options)
    {
        if (_attempted) return null;
        var component = menu.gameObject.AddComponent<ReleaseNotesWindow>();
        component._menu = menu;
        component._manifest = manifest;
        component._options = options;
        return component;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (_window != null)
        {
            // Disabling the mod is not an acknowledgement from the player.
            _window.onClose = null;
            _window.Close();
            _window = null;
        }
    }

    private IEnumerator Start()
    {
        yield return LocalizationSettings.InitializationOperation;
        var locale = LocalizationSettings.AvailableLocales.GetLocale(LocHelper.ResolveStartupLocale())
            ?? LocalizationSettings.AvailableLocales.GetLocale("en");
        // Vanilla applies its saved choice in a separate coroutine after localization first initializes.
        while (LocalizationSettings.SelectedLocale != locale) yield return null;
        yield return LocalizationSettings.InitializationOperation;
        // Let the menu finish deciding whether to show its welcome panel, even when the locale was unchanged.
        yield return null;
        while (CustomUIManager.Instance == null || (_menu.welcomePanel != null && _menu.welcomePanel.activeInHierarchy))
            yield return null;
        _attempted = true;
        ShowNotes(_manifest, _options);
    }

    private void ShowNotes(ModManifest manifest, Options options)
    {
        bool development = ReleaseNotes.IsDevelopment;
        string? acknowledged = null;
        if (!development)
        {
            try { acknowledged = ModHelper.ReadModDataFileText(manifest.ID, VersionFile); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning($"Could not read the acknowledged release: {ex.Message}");
            }
        }
        if (!ReleaseNotes.ShouldShow(manifest.ModVersion, acknowledged, development)) return;

        var manager = CustomUIManager.Instance;
        var builder = manager.CreateWindow("tuck:release-notes", $"{manifest.Name} {manifest.ModVersion} {options.Text("changelog")}",
            CustomUIManager.LAYER_MENU, manager.optionUIPrefab).SetCloseOnEscape();
        _window = builder.Handle;
        _window.frame.titleText.enableWordWrapping = false;
        _window.frame.titleText.overflowMode = TextOverflowModes.Overflow;
        builder.BeginScrollFlexible();
        builder.AddLabel(ReleaseNotes.ReadChangelog());
        builder.Last.SetFlexible(1f, 0f);
        builder.Last.text.alignment = TextAlignmentOptions.TopLeft;
        builder.Last.text.richText = true;
        builder.Last.text.enableWordWrapping = true;
        builder.Last.text.overflowMode = TextOverflowModes.Overflow;
        builder.End();
        builder.AddSeparator();
        builder.BeginRow(12f, TextAnchor.MiddleCenter);
        builder.AddFlexibleSpace();
        AddFeedbackButton(builder, options);
        builder.AddButton(options.Text("close"), builder.Handle.Close);
        builder.AddFlexibleSpace();
        builder.End();
        builder.OnClose(() =>
        {
            _window = null;
            if (development) return;
            try { ModHelper.WriteModDataFileText(manifest.ID, VersionFile, manifest.ModVersion); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning($"Could not save the acknowledged release: {ex.Message}");
            }
        });
        builder.Show();
    }

    private static void AddFeedbackButton(CustomUIBuilder builder, Options options)
    {
        builder.AddButton("", CopyDiscordLink, "copy_discord_link");
        var button = builder.Last;
        string note = RenderHandler.ApplyRichTextColorTag(
            options.Text("avoids_the_mod_requiring_permission_to_open_the_link"), RenderHandler.ColorPalette.Gray);
        button.SetFixedWidth(240f).SetTooltip($"{options.Text("copies_tucks_discord_link")}\n<i>{note}</i>");
        var content = new CustomUIBuilder(CustomUIManager.Instance, builder.Handle, button.Rect);
        content.BeginRow(6f, TextAnchor.MiddleCenter);
        var row = content.CurrentContainer;
        row.anchorMin = Vector2.zero;
        row.anchorMax = Vector2.one;
        row.offsetMin = row.offsetMax = Vector2.zero;
        var label = UnityEngine.Object.Instantiate(button.text, row, false);
        label.text = options.Text("feedback");
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        var discordIcon = MainMenuUIController.Instance.discordButton.transform.Find("Discord").GetComponent<Image>();
        content.AddImage(discordIcon.sprite, 28f, 24f);
        content.Last.image.preserveAspect = true;
        content.Last.image.color = button.text.color;
        content.Last.image.raycastTarget = false;
        content.End();
        button.text.gameObject.SetActive(false);
    }

    private static void CopyDiscordLink() =>
        GUIUtility.systemCopyBuffer = "https://discord.com/channels/1315752321975845004/1569589459458981988";
}
