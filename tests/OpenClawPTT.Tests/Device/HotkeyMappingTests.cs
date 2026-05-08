using System; using OpenClawPTT; using Xunit;

namespace OpenClawPTT.Tests;

public class HotkeyMappingTests
{
    [Fact] public void Parse_AltEquals_HasAltModifierAndEqualKey() {
        var h = HotkeyMapping.Parse("Alt+=");
        Assert.Contains(Modifier.Alt, h.Modifiers); Assert.Equal(Key.Equal, h.Key);
    }
    [Fact] public void Parse_CtrlShiftSpace_AllThree() {
        var h = HotkeyMapping.Parse("Ctrl+Shift+Space");
        Assert.Contains(Modifier.Ctrl, h.Modifiers); Assert.Contains(Modifier.Shift, h.Modifiers);
        Assert.Equal(Key.Space, h.Key);
    }
    [Fact] public void Parse_SingleLetter() {
        var h = HotkeyMapping.Parse("A"); Assert.Empty(h.Modifiers); Assert.Equal(new Key('A'), h.Key);
    }
    [Fact] public void Parse_F12_SpecialKey() {
        var h = HotkeyMapping.Parse("F12"); Assert.Empty(h.Modifiers); Assert.Equal(Key.F12, h.Key);
    }
    [Fact] public void Parse_Minus_ReturnsMinusSpecialKey() {
        Assert.Equal(SpecialKey.Minus, HotkeyMapping.Parse("-").Key.Special);
    }
    [Fact] public void Parse_LowercaseModifiers_Valid() {
        var h = HotkeyMapping.Parse("ctrl+alt+a");
        Assert.Contains(Modifier.Ctrl, h.Modifiers); Assert.Contains(Modifier.Alt, h.Modifiers);
    }
    [Fact] public void Parse_ControlAlias_IsCtrl() {
        var h = HotkeyMapping.Parse("Control+Shift+3");
        Assert.Contains(Modifier.Ctrl, h.Modifiers); Assert.Contains(Modifier.Shift, h.Modifiers);
    }
    [Fact] public void Parse_MetaAlias_IsWin() {
        Assert.Contains(Modifier.Win, HotkeyMapping.Parse("Meta+D").Modifiers);
    }
    [Fact] public void Parse_SuperAlias_IsWin() {
        Assert.Contains(Modifier.Win, HotkeyMapping.Parse("Super+E").Modifiers);
    }
    [Fact] public void Parse_EmptyString_Throws() { Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse("")); }
    [Fact] public void Parse_WhitespaceOnly_Throws() { Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse("   ")); }
    [Fact] public void Parse_UnknownModifier_Throws() { Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse("Garbage+A")); }
    [Fact] public void Parse_InvalidFormat_NoModifierNoKey_Throws() { Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse("garbage")); }
    [Fact] public void Parse_UnknownKey_Throws() { Assert.Throws<ArgumentException>(() => HotkeyMapping.Parse("Ctrl+UnknownKey")); }
    [Fact] public void Parse_ValidFormat_ReturnsHotkey() {
        var h = HotkeyMapping.Parse("Ctrl+Shift+A");
        Assert.Contains(Modifier.Ctrl, h.Modifiers); Assert.Contains(Modifier.Shift, h.Modifiers);
        Assert.Equal(new Key('A'), h.Key);
    }
    [Fact] public void Parse_MultipleModifiers_ReturnsHotkey() {
        var h = HotkeyMapping.Parse("Ctrl+Alt+Shift+A");
        Assert.Contains(Modifier.Ctrl, h.Modifiers); Assert.Contains(Modifier.Alt, h.Modifiers);
        Assert.Contains(Modifier.Shift, h.Modifiers); Assert.Equal(new Key('A'), h.Key);
    }
    [Fact] public void Parse_CaseInsensitivity_ReturnsHotkey() {
        var lower = HotkeyMapping.Parse("ctrl+a"); var upper = HotkeyMapping.Parse("CTRL+A");
        Assert.Equal(lower.Key, upper.Key); Assert.Equal(lower.Modifiers.Count, upper.Modifiers.Count);
    }
    [Theory]
    [InlineData("Ctrl+Shift+A", 'A')] [InlineData("Ctrl+B", 'B')] [InlineData("Ctrl+1", '1')]
    public void GetPlatformKeyCode_ValidKey_ReturnsPlatformKeyCode(string c, char e) {
        Assert.True(HotkeyMapping.GetPlatformKeyCode(HotkeyMapping.Parse(c).Key) > 0, $"Expected positive for {e}");
    }
    [Fact] public void GetPlatformModifierFlags_ValidModifiers_ReturnsComputedMask() {
        HotkeyMapping.GetPlatformModifierFlags(HotkeyMapping.Parse("Ctrl+Shift+A").Modifiers);
    }
    [Fact] public void GetPlatformModifierFlags_NoModifiers_ReturnsZero() {
        Assert.Equal(0UL, HotkeyMapping.GetPlatformModifierFlags(HotkeyMapping.Parse("A").Modifiers));
    }
    [Theory] [InlineData('a','A')] [InlineData('z','Z')] [InlineData('1','1')]
    public void Key_CharConstructor_UppercasesValue(char input, char expected) { Assert.Equal(expected, new Key(input).Value); }
    [Fact] public void Key_CharConstructor_SpecialIsNone() { Assert.Equal(SpecialKey.None, new Key('Z').Special); }
    [Fact] public void Key_SpecialKeys_HaveCorrectSpecial() {
        Assert.Equal(SpecialKey.Space, Key.Space.Special); Assert.Equal(SpecialKey.Equal, Key.Equal.Special);
        Assert.Equal(SpecialKey.Minus, Key.Minus.Special); Assert.Equal(SpecialKey.F1, Key.F1.Special);
        Assert.Equal(SpecialKey.F12, Key.F12.Special);
    }
    [Fact] public void Key_SpecialKeys_HaveZeroCharValue() {
        Assert.Equal('\0', Key.Space.Value); Assert.Equal('\0', Key.Equal.Value); Assert.Equal('\0', Key.F1.Value);
    }
    [Theory]
    [InlineData("Space","Space")] [InlineData("Equal","Equal")] [InlineData("Minus","Minus")]
    [InlineData("F1","F1")] [InlineData("F12","F12")]
    public void Key_SpecialToString_ReturnsSpecialName(string e, string n) {
        Key k = n switch { "Space"=>Key.Space,"Equal"=>Key.Equal,"Minus"=>Key.Minus,"F1"=>Key.F1,"F12"=>Key.F12,_=>throw new ArgumentException(n)};
        Assert.Equal(e, k.ToString());
    }
    [Fact] public void Key_CharToString_ReturnsChar() { Assert.Equal("A", new Key('a').ToString()); Assert.Equal("Z", new Key('Z').ToString()); }
    [Fact] public void Key_Equals_SameChars() { Assert.Equal(new Key('A'), new Key('a')); Assert.True(new Key('A')==new Key('a')); Assert.False(new Key('A')!=new Key('a')); }
    [Fact] public void Key_NotEquals_DifferentChars() { Assert.NotEqual(new Key('A'), new Key('B')); }
    [Fact] public void Key_GetHashCode_UppercaseSensitive() { Assert.Equal(new Key('A').GetHashCode(), new Key('a').GetHashCode()); }
    [Fact] public void Key_StaticFactories_AllFKeys() { Assert.Equal(SpecialKey.F1,Key.F1.Special); Assert.Equal(SpecialKey.F2,Key.F2.Special); Assert.Equal(SpecialKey.F3,Key.F3.Special); Assert.Equal(SpecialKey.F10,Key.F10.Special); Assert.Equal(SpecialKey.F11,Key.F11.Special); Assert.Equal(SpecialKey.F12,Key.F12.Special); }
    [Fact] public void Modifier_AllFourValues() { Assert.Equal(0,(int)Modifier.Alt); Assert.Equal(1,(int)Modifier.Ctrl); Assert.Equal(2,(int)Modifier.Shift); Assert.Equal(3,(int)Modifier.Win); }
}
