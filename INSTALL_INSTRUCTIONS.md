# Polling Interval Update - Installation Instructions

## ✅ Files Updated Based on Your GitHub Source

I've applied the changes to YOUR actual source code from the zip file.

**Namespace verified:** `nvGPUMonitor` (lowercase 'nv') ✓

## 📦 Files to Replace

Simply replace these 2 files in your project:

1. **MainWindow.xaml** - Added interval dropdown ComboBox
2. **MainWindow.xaml.cs** - Added `IntervalComboBox_SelectionChanged` handler

## 🔧 What Changed

### MainWindow.xaml
Added before the "Folder To Monitor" button:
```xml
<TextBlock Text="Update Interval:" VerticalAlignment="Center" Margin="0,0,8,0" FontWeight="SemiBold"/>
<ComboBox x:Name="IntervalComboBox" Width="100" Margin="0,0,16,0" SelectionChanged="IntervalComboBox_SelectionChanged">
  <ComboBoxItem Content="0.1 sec" Tag="100"/>
  <ComboBoxItem Content="0.2 sec" Tag="200"/>
  ...
  <ComboBoxItem Content="1.0 sec" Tag="1000" IsSelected="True"/>
  ...
  <ComboBoxItem Content="5.0 sec" Tag="5000"/>
</ComboBox>
```

### MainWindow.xaml.cs
Added new event handler at the end (before the closing braces):
```csharp
private void IntervalComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
{
    if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
    {
        if (item.Tag is string intervalStr && int.TryParse(intervalStr, out int intervalMs))
        {
            _tick.Stop();
            _tick.Interval = intervalMs;
            _tick.Start();
        }
    }
}
```

## 🚀 Installation Steps

1. **Backup your current files** (just in case)
2. **Replace MainWindow.xaml** with the new version
3. **Replace MainWindow.xaml.cs** with the new version
4. **Rebuild** your project
5. **Run** the application

## ✨ Result

You'll see the interval dropdown in the bottom-right:

```
[Update Interval: ▼ 1.0 sec] [Folder To Monitor] [Start Log] [Stop Log]
```

## 📊 Available Intervals

**Fine-grained (GPU Profiling):**
- 0.1s, 0.2s, 0.3s, 0.4s, 0.5s, 0.6s, 0.7s, 0.8s, 0.9s, 1.0s

**Coarse (Low CPU Usage):**
- 2.0s, 3.0s, 4.0s, 5.0s

**Default:** 1.0 second

## 🧪 Testing

1. Build and run the application
2. Select different intervals from the dropdown
3. Watch the gauges update at the selected rate
4. Verify all metrics update correctly

## ❓ Troubleshooting

**If it doesn't build:**
- Make sure you replaced BOTH files
- Check that the namespace is `nvGPUMonitor` (lowercase 'nv')
- Clean and rebuild the solution

**If it builds but crashes:**
- Check the error message
- Verify the ComboBox name is `IntervalComboBox`
- Ensure the SelectionChanged event is connected

**If the dropdown doesn't appear:**
- Check MainWindow.xaml was updated correctly
- Verify the StackPanel section was replaced

## 📝 Notes

- Changes take effect immediately (no restart needed)
- The interval persists while the app is running
- Resets to default (1.0s) when you restart the app
- Lower intervals (0.1-0.5s) will use more CPU but provide finer detail
- Higher intervals (2-5s) reduce CPU usage for background monitoring
