using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

[TestFixture]
public class ConsoleUiNavigationEditModeTests
{
    [TestCase(DeviceType.Console, true)]
    [TestCase(DeviceType.Desktop, false)]
    [TestCase(DeviceType.Handheld, false)]
    [TestCase(DeviceType.Unknown, false)]
    public void IsConsoleDevice_OnlyEnablesConsoleHardware(
        DeviceType deviceType,
        bool expected
    )
    {
        Assert.That(ConsoleUiNavigation.IsConsoleDevice(deviceType), Is.EqualTo(expected));
    }

    [Test]
    public void ConfigureButtons_DisablesButtonFocusOutsideConsole()
    {
        VisualElement root = new();
        Button button = new() { focusable = true };
        root.Add(button);

        ConsoleUiNavigation.ConfigureButtons(root, DeviceType.Desktop);

        Assert.That(button.focusable, Is.False);
    }

    [Test]
    public void ConfigureButtons_PreservesAuthoredFocusOnConsole()
    {
        VisualElement root = new();
        Button navigationButton = new() { focusable = true };
        Button intentionallySkippedButton = new() { focusable = false };
        root.Add(navigationButton);
        root.Add(intentionallySkippedButton);

        ConsoleUiNavigation.ConfigureButtons(root, DeviceType.Console);

        Assert.That(navigationButton.focusable, Is.True);
        Assert.That(intentionallySkippedButton.focusable, Is.False);
    }
}
