using System.Globalization;
using MusicVisualizer.Settings;
using MusicVisualizer.Visuals;
using SkiaSharp;

namespace MusicVisualizer.Tests;

public class SettingsTests
{
    private const string OldFormat = """{"Visualizer":"Bars","PaletteIndex":3,"Glow":false}""";

    [Fact]
    public void Old_settings_file_migrates_palette_index_to_name_and_defaults_new_fields()
    {
        var s = AppSettings.FromJson(OldFormat);

        Assert.Equal("Bars", s.Visualizer);
        Assert.Equal(Palette.All[3].Name, s.PaletteName);
        Assert.Equal("Fire", s.PaletteName);
        Assert.False(s.Glow);

        Assert.Equal(CycleMode.Off, s.CycleVisualizers);
        Assert.Equal(ColorCycleMode.Off, s.CycleColors);
        Assert.Equal(3, s.CycleMinutes);
        Assert.True(s.FadeTransitions);
        Assert.False(s.Shuffle);
        Assert.Equal(1.0, s.OverlayOpacity);
        Assert.Empty(s.DisabledVisualizers);
        Assert.Empty(s.DisabledPalettes);
        Assert.Empty(s.CustomPalettes);
    }

    [Fact]
    public void Out_of_range_palette_index_clamps_to_the_last_built_in()
    {
        var s = AppSettings.FromJson("""{"PaletteIndex":99}""");
        Assert.Equal(Palette.All[^1].Name, s.PaletteName);
    }

    [Fact]
    public void Migrated_settings_do_not_write_palette_index()
    {
        var json = AppSettings.FromJson(OldFormat).ToJson();
        Assert.DoesNotContain("PaletteIndex", json);
        Assert.Contains("\"PaletteName\"", json);
        Assert.Equal("Fire", AppSettings.FromJson(json).PaletteName);
    }

    [Fact]
    public void Palette_name_wins_over_palette_index_when_both_present()
    {
        var s = AppSettings.FromJson("""{"PaletteName":"Ice","PaletteIndex":3}""");
        Assert.Equal("Ice", s.PaletteName);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    public void Corrupt_json_gives_defaults(string json)
    {
        var s = AppSettings.FromJson(json);
        Assert.Equal("Neon", s.PaletteName);
        Assert.Equal("Radial", s.Visualizer);
        Assert.True(s.Glow);
        Assert.Equal(CycleMode.Off, s.CycleVisualizers);
    }

    [Theory]
    [InlineData(0.0, AppSettings.MinCycleMinutes)]
    [InlineData(-5.0, AppSettings.MinCycleMinutes)]
    [InlineData(1000.0, AppSettings.MaxCycleMinutes)]
    [InlineData(5.0, 5.0)]
    public void Cycle_minutes_are_clamped(double stored, double expected)
    {
        var s = AppSettings.FromJson($$"""{"CycleMinutes":{{stored.ToString(CultureInfo.InvariantCulture)}}}""");
        Assert.Equal(expected, s.CycleMinutes);
    }

    [Theory]
    [InlineData(0.0, AppSettings.MinOverlayOpacity)]
    [InlineData(5.0, 1.0)]
    [InlineData(0.5, 0.5)]
    public void Overlay_opacity_is_clamped(double stored, double expected)
    {
        var s = AppSettings.FromJson($$"""{"OverlayOpacity":{{stored.ToString(CultureInfo.InvariantCulture)}}}""");
        Assert.Equal(expected, s.OverlayOpacity);
    }

    [Theory]
    [InlineData(0.0, AppSettings.MinMicrophoneGain)]
    [InlineData(500.0, AppSettings.MaxMicrophoneGain)]
    [InlineData(6.5, 6.5)]
    public void Microphone_gain_is_clamped(double stored, double expected)
    {
        var s = AppSettings.FromJson($$"""{"MicrophoneGain":{{stored.ToString(CultureInfo.InvariantCulture)}}}""");
        Assert.Equal(expected, s.MicrophoneGain);
    }

    [Fact]
    public void Old_settings_default_to_pc_audio_and_the_default_microphone()
    {
        var s = AppSettings.FromJson("""{"Visualizer":"Bars","MicrophoneId":"  "}""");
        Assert.Equal(MusicVisualizer.Audio.AudioSource.Speakers, s.AudioSource);
        Assert.Null(s.MicrophoneId);
        Assert.Equal(AppSettings.DefaultMicrophoneGain, s.MicrophoneGain);
    }

    [Fact]
    public void Microphone_choice_round_trips()
    {
        var s = new AppSettings { AudioSource = MusicVisualizer.Audio.AudioSource.Microphone, MicrophoneId = "{0.0.1.00000000}.{abc}" };
        var back = AppSettings.FromJson(s.ToJson());
        Assert.Equal(MusicVisualizer.Audio.AudioSource.Microphone, back.AudioSource);
        Assert.Equal("{0.0.1.00000000}.{abc}", back.MicrophoneId);
        Assert.Contains("\"Microphone\"", s.ToJson()); // stored by name, like the other enums
    }

    [Fact]
    public void Enums_round_trip_as_strings()
    {
        var s = AppSettings.FromJson("""{"CycleVisualizers":"Song","CycleColors":"WithVisualizer","Mode":"Overlay"}""");
        Assert.Equal(CycleMode.Song, s.CycleVisualizers);
        Assert.Equal(ColorCycleMode.WithVisualizer, s.CycleColors);
        Assert.Equal(DisplayMode.Overlay, s.Mode);

        var json = s.ToJson();
        Assert.Contains("\"CycleVisualizers\": \"Song\"", json);
        Assert.Contains("\"CycleColors\": \"WithVisualizer\"", json);

        var back = AppSettings.FromJson(json);
        Assert.Equal(CycleMode.Song, back.CycleVisualizers);
        Assert.Equal(ColorCycleMode.WithVisualizer, back.CycleColors);
    }

    private static CustomPaletteData Custom(string name, params string[] colors) => new() { Name = name, Colors = [.. colors] };

    [Fact]
    public void Valid_custom_palette_is_built_with_parsed_colors()
    {
        var s = new AppSettings { CustomPalettes = [Custom("Mine", "#FF0000", "00ff00", "#0000FF")] };

        var p = Assert.Single(s.BuildCustomPalettes());
        Assert.Equal("Mine", p.Name);
        Assert.True(p.IsCustom);
        Assert.Equal(new[] { new SKColor(255, 0, 0), new SKColor(0, 255, 0), new SKColor(0, 0, 255) }, p.Colors);
    }

    [Fact]
    public void Invalid_custom_palettes_are_skipped()
    {
        var s = new AppSettings
        {
            CustomPalettes =
            [
                Custom("", "#FF0000", "#00FF00"),               // empty name
                Custom("   ", "#FF0000", "#00FF00"),            // blank name
                Custom("neon", "#FF0000", "#00FF00"),           // clashes with built-in Neon
                Custom("Good", "#FF0000", "#00FF00"),
                Custom("good", "#112233", "#445566"),           // duplicate of Good
                Custom("One", "#FF0000"),                       // too few colours
                Custom("Junk", "#FF0000", "zzzzzz", "#FFF"),    // only one valid colour
            ],
        };

        var p = Assert.Single(s.BuildCustomPalettes());
        Assert.Equal("Good", p.Name);
        Assert.Equal(new[] { new SKColor(255, 0, 0), new SKColor(0, 255, 0) }, p.Colors);
    }

    [Fact]
    public void Invalid_hex_strings_are_ignored_but_the_palette_kept_if_enough_remain()
    {
        var s = new AppSettings { CustomPalettes = [Custom("Mixed", "#FF0000", "nope", "#0000FF")] };

        var p = Assert.Single(s.BuildCustomPalettes());
        Assert.Equal(new[] { new SKColor(255, 0, 0), new SKColor(0, 0, 255) }, p.Colors);
    }

    [Fact]
    public void Custom_palettes_round_trip_through_json()
    {
        var s = new AppSettings { CustomPalettes = [Custom("Mine", "#FF8800", "#0088FF")] };
        var back = AppSettings.FromJson(s.ToJson());

        var data = Assert.Single(back.CustomPalettes);
        Assert.Equal("Mine", data.Name);
        Assert.Equal(new[] { "#FF8800", "#0088FF" }, data.Colors.ToArray());
    }

    [Theory]
    [InlineData("#ff8800")]
    [InlineData("FF8800")]
    [InlineData(" #FF8800 ")]
    public void TryParseHex_accepts_six_digit_hex(string text)
    {
        Assert.True(Palette.TryParseHex(text, out var c));
        Assert.Equal(new SKColor(0xFF, 0x88, 0x00), c);
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("zzzzzz")]
    [InlineData("")]
    [InlineData("#FF88001")]
    [InlineData(null)]
    public void TryParseHex_rejects_invalid_text(string? text)
    {
        Assert.False(Palette.TryParseHex(text, out _));
    }

    [Fact]
    public void ToHex_round_trips()
    {
        var c = new SKColor(0x12, 0xAB, 0xEF);
        Assert.Equal("#12ABEF", Palette.ToHex(c));
        Assert.True(Palette.TryParseHex(Palette.ToHex(c), out var back));
        Assert.Equal(c, back);
    }

    [Fact]
    public void Create_with_one_color_throws()
    {
        Assert.Throws<ArgumentException>(() => Palette.Create("Solo", [SKColors.Red]));
    }

    [Fact]
    public void Create_caps_colors_at_max()
    {
        var colors = Enumerable.Range(0, Palette.MaxColors + 3).Select(i => new SKColor((byte)(i * 20), 0, 0));
        var p = Palette.Create("Many", colors);
        Assert.Equal(Palette.MaxColors, p.Colors.Length);
        Assert.True(p.IsCustom);
    }

    [Fact]
    public void Create_forces_colors_opaque()
    {
        var p = Palette.Create("Clear", [new SKColor(255, 0, 0, 10), new SKColor(0, 255, 0, 0)]);
        Assert.All(p.Colors, c => Assert.Equal((byte)255, c.Alpha));
    }
}
