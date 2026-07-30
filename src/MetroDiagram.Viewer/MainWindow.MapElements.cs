using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MetroDiagram.Rendering;

namespace MetroDiagram.Viewer;

public partial class MainWindow
{
    private bool _mapElementsPanelOpen;

    private void MapElements_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        _mapElementsPanelOpen = true;
        _suppressUiEvents = true;
        try
        {
            ManualEditCheckBox.IsChecked = true;
            ManualEditIconsItem.IsSelected = true;
        }
        finally
        {
            _suppressUiEvents = false;
        }

        UpdateManualEditButtons();
        if (!string.IsNullOrWhiteSpace(_currentSvg))
        {
            WritePreviewHtml(_currentSvg);
        }

        SetStatus(T("MapElementsHelp"));
    }

    private void CloseMapElements_Click(object sender, RoutedEventArgs e)
    {
        _mapElementsPanelOpen = false;
        UpdateManualEditButtons();
    }

    private void ApplyMapElementText_Click(object sender, RoutedEventArgs e)
    {
        if (_layoutOverrides is null
            || string.IsNullOrWhiteSpace(_selectedOverrideStationId)
            || !_layoutOverrides.Icons.TryGetValue(_selectedOverrideStationId, out MapIconLayoutOverride? icon))
        {
            SetStatus(T("ManualEditNoSelection"));
            return;
        }

        try
        {
            PushUndoSnapshot();
            string title = MapElementTitleTextBox.Text.Trim();
            string text = MapElementTextTextBox.Text.Trim();
            icon.Title = title.Length == 0 ? null : title;
            icon.Text = text.Length == 0 ? null : text;
            if (!string.Equals(icon.Type, "information-box", StringComparison.OrdinalIgnoreCase))
            {
                icon.Label = title.Length == 0 ? null : title;
            }

            SaveOrDeleteLayoutOverrides();
            MarkPreviewDirty();
            RenderPreview();
            SetStatus(string.Format(
                CultureInfo.CurrentCulture,
                T("MapElementUpdated"),
                T(GetIconTypeResourceKey(icon.Type))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("OverridesSaveFailed"), ex.Message));
        }
    }

    private void PopulateMapElementEditor(string iconId)
    {
        if (_layoutOverrides?.Icons.TryGetValue(iconId, out MapIconLayoutOverride? icon) != true
            || icon is null)
        {
            return;
        }

        MapElementTitleTextBox.Text = icon.Title ?? icon.Label ?? string.Empty;
        MapElementTextTextBox.Text = icon.Text ?? string.Empty;

        foreach (object item in IconTypeComboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && string.Equals(comboBoxItem.Tag?.ToString(), icon.Type, StringComparison.OrdinalIgnoreCase))
            {
                IconTypeComboBox.SelectedItem = comboBoxItem;
                break;
            }
        }
    }
}
