using System.Numerics;
using MusicVisualizer.Audio;
using SkiaSharp;

namespace MusicVisualizer.Visuals.Disaster;

// The rooms. Each layout adds wall/floor art, furniture, props and an escalating list of disasters:
// level 1 = something small falls, 2 = things slide off, 3 = something structural fails, 4 = the big finale.
// The dancer's spot (Room.ClearLeft..ClearRight) stays clear; a few things hang right above it.
internal sealed partial class Room
{
    private enum WallStyle { Stripes, Logs, Tiles, Plain, Dots, Damask }
    private enum FloorStyle { Planks, Checker, Carpet }
    private enum Outside { Sunset, Aurora, SnowNight, City, Sunny }

    private void BuildLayout()
    {
        switch (ThemeName)
        {
            case "Neon": GamerRoom(); break;
            case "Aurora": Cabin(); break;
            case "Fire": Kitchen(); break;
            case "Ice": Office(); break;
            case "Christmas":
            case "Candy Cane": ChristmasRoom(); break;
            case "Rainbow": Playroom(); break;
            default: LivingRoom(); break;
        }
    }

    // ================================================================== rooms

    private void LivingRoom()
    {
        var warm = _palette.Name == "Sunset" ? _palette.Sample(0.35f) : new SKColor(0xE0, 0x7A, 0x5F);
        Walls(new SKColor(0xF4, 0xE6, 0xCC), new SKColor(0xE6, 0xCE, 0xA8), WallStyle.Stripes, new SKColor(0xE0, 0xC6, 0x9E));
        Floor(new SKColor(0x9A, 0x62, 0x3E), FloorStyle.Planks);
        Rug(2.2f, 5.9f, warm, new SKColor(0xF0, 0xD8, 0xB0));
        Window(new SKRect(0.45f, 0.75f, 2.05f, 2.55f), Outside.Sunset);

        // couch (its seat is a surface things can land on)
        var couch = PropArt.Shade(warm, 0.85f);
        Furniture(new SKRect(5.55f, 2.8f, 7.95f, FloorY), c =>
        {
            RoundRect(c, PropArt.Shade(couch, 0.8f), 5.65f, 2.85f, 7.85f, 3.6f, 0.12f);   // back
            RoundRect(c, couch, 5.55f, 3.35f, 7.95f, 3.95f, 0.1f);                        // seat block
            RoundRect(c, PropArt.Shade(couch, 1.12f), 5.8f, 3.3f, 6.82f, 3.5f, 0.08f);     // cushions
            RoundRect(c, PropArt.Shade(couch, 1.12f), 6.86f, 3.3f, 7.75f, 3.5f, 0.08f);
            RoundRect(c, PropArt.Shade(couch, 0.9f), 5.5f, 3.1f, 5.8f, 3.95f, 0.1f);       // arms
            RoundRect(c, PropArt.Shade(couch, 0.9f), 7.7f, 3.1f, 8.0f, 3.95f, 0.1f);
            Rect(c, new SKColor(0x4A, 0x30, 0x22), 5.65f, 3.95f, 5.75f, FloorY);
            Rect(c, new SKColor(0x4A, 0x30, 0x22), 7.75f, 3.95f, 7.85f, FloorY);
        });
        Surface(5.8f, 7.75f, 3.3f);

        // shelf above the couch, loaded with books
        var shelf = Shelf(5.5f, 7.6f, 1.85f, new SKColor(0x8A, 0x5A, 0x36), snapRight: false);
        var books = new List<Prop>();
        SKColor[] spines = [new(0x3E, 0x6E, 0x8E), new(0xB8, 0x4A, 0x3A), new(0xD8, 0xA8, 0x48), new(0x4E, 0x7A, 0x4A), new(0x7A, 0x4A, 0x7E), warm];
        float bx = 5.72f;
        for (int i = 0; i < 6; i++)
        {
            float bh = 0.34f + 0.06f * ((i * 7) % 3);
            books.Add(Box(PropKind.Book, bx, 1.85f, 0.11f, bh, 0.5f, spines[i], new SKColor(0xF0, 0xE0, 0xB8), RattleMode.Rock));
            bx += 0.135f;
        }
        var vase = Box(PropKind.Vase, 6.95f, 1.85f, 0.26f, 0.38f, 0.8f, new SKColor(0x3E, 0x8A, 0x8C), new SKColor(0xF0, 0xE0, 0xB8), RattleMode.Rock);
        var clockBox = Box(PropKind.Clock, 7.35f, 1.85f, 0.3f, 0.3f, 0.6f, new SKColor(0x8A, 0x5A, 0x36), warm, RattleMode.Creep);
        clockBox.CreepDir = -1;

        var frameA = Hang(PropKind.Frame, new Vector2(2.7f, 1.5f), 0.7f, 0.55f, 1.2f, new Vector2(2.7f, 1.0f), new SKColor(0xC8, 0x9A, 0x5A), new SKColor(0x6A, 0xA8, 0xD8));
        var frameB = Hang(PropKind.Frame, new Vector2(4.0f, 1.08f), 0.58f, 0.46f, 1f, new Vector2(4.0f, 0.62f), new SKColor(0x5A, 0x3E, 0x2E), warm);

        var lamp = Box(PropKind.FloorLamp, 2.5f, FloorY, 0.44f, 1.75f, 4f, new SKColor(0xF4, 0xDC, 0xA8), warm, RattleMode.None);
        var plant = Box(PropKind.Plant, 1.15f, FloorY, 0.55f, 0.95f, 3f, new SKColor(0xC0, 0x6A, 0x44), warm, RattleMode.Rock);
        var pendant = Hang(PropKind.Pendant, new Vector2(4.65f, 1.25f), 0.55f, 0.32f, 2f, new Vector2(4.65f, 0f), new SKColor(0xE8, 0xB0, 0x5A), warm);

        Light(lamp, new Vector2(0, -0.6f), new SKColor(0xFF, 0xD8, 0x90), 0.9f, LightKind.Bass);
        Light(pendant, new Vector2(0, 0.2f), new SKColor(0xFF, 0xE2, 0xA0), 0.8f, LightKind.Steady);

        Disaster(1, () => Drop(frameB, 0.1f));
        Disaster(1, () => Drop(frameA, 0.2f));
        Disaster(2, () => Slide(books.Concat([vase, clockBox]), -1, 2.4f));
        Disaster(3, () => Snap(shelf, books.Concat([vase, clockBox])));
        Disaster(3, () => Topple(lamp, 1, 2.6f));
        Disaster(3, () => Topple(plant, 1, 2.4f));
        Disaster(4, () => Drop(pendant));
    }

    private void GamerRoom()
    {
        Walls(new SKColor(0x1C, 0x16, 0x34), new SKColor(0x10, 0x0C, 0x20), WallStyle.Plain, new SKColor(0x26, 0x1E, 0x44));
        Floor(new SKColor(0x24, 0x1E, 0x36), FloorStyle.Carpet);
        Poster(new SKRect(0.55f, 0.75f, 1.6f, 2.05f), 0);
        Poster(new SKRect(6.6f, 0.65f, 7.55f, 1.75f), 1);
        LedStrip(0.14f);

        // desk with a keyboard, chair in front
        Furniture(new SKRect(5.1f, 2.9f, 8f, FloorY), c =>
        {
            Rect(c, new SKColor(0x2A, 0x26, 0x3A), 5.15f, 2.95f, 7.95f, 3.05f);
            Rect(c, new SKColor(0x22, 0x1E, 0x30), 5.25f, 3.05f, 5.35f, FloorY);
            Rect(c, new SKColor(0x22, 0x1E, 0x30), 7.75f, 3.05f, 7.85f, FloorY);
            RoundRect(c, new SKColor(0x2E, 0x2A, 0x40), 6.9f, 3.05f, 7.75f, 3.6f, 0.03f);   // drawers
            RoundRect(c, new SKColor(0x18, 0x16, 0x22), 6.2f, 2.88f, 6.95f, 2.95f, 0.02f);   // keyboard
            // gaming chair
            RoundRect(c, PropArt.Shade(_palette.Sample(0.8f), 0.5f), 5.45f, 2.55f, 5.95f, 3.45f, 0.12f);
            RoundRect(c, new SKColor(0x1A, 0x18, 0x24), 5.4f, 3.4f, 6.05f, 3.55f, 0.05f);
            Rect(c, new SKColor(0x1A, 0x18, 0x24), 5.69f, 3.55f, 5.77f, 4.05f);
            Rect(c, new SKColor(0x1A, 0x18, 0x24), 5.45f, 4.05f, 6.0f, 4.12f);
        });
        Surface(5.15f, 7.95f, 2.95f);

        var monitor = Box(PropKind.Monitor, 7.1f, 2.95f, 0.95f, 0.7f, 3f, new SKColor(0x1C, 0x1C, 0x22), _palette.Sample(0.5f), RattleMode.None);
        var cans = new[]
        {
            Box(PropKind.Can, 5.45f, 2.95f, 0.09f, 0.17f, 0.3f, _palette.Sample(0f), new SKColor(0x10, 0x10, 0x10), RattleMode.Hop),
            Box(PropKind.Can, 5.62f, 2.95f, 0.09f, 0.17f, 0.3f, _palette.Sample(1f), new SKColor(0x10, 0x10, 0x10), RattleMode.Hop),
        };
        var phones = Box(PropKind.Headphones, 5.95f, 2.95f, 0.32f, 0.24f, 0.5f, new SKColor(0x26, 0x26, 0x30), _palette.Sample(0.3f), RattleMode.Creep);
        phones.CreepDir = -1;

        var shelf = Shelf(0.4f, 2.3f, 2.4f, new SKColor(0x3A, 0x30, 0x52), snapRight: true);
        var trophy = Box(PropKind.Trophy, 0.75f, 2.4f, 0.24f, 0.4f, 0.8f, new SKColor(0xE8, 0xC0, 0x48), new SKColor(0xFF, 0xF0, 0xA0), RattleMode.Rock);
        var shelfStuff = new List<Prop> { trophy };
        shelfStuff.Add(Box(PropKind.Can, 1.15f, 2.4f, 0.09f, 0.17f, 0.3f, _palette.Sample(0.6f), new SKColor(0x10, 0x10, 0x10)));
        shelfStuff.Add(Box(PropKind.Block, 1.5f, 2.4f, 0.22f, 0.22f, 0.4f, _palette.Sample(0.2f), _palette.Sample(0.8f), RattleMode.Rock));
        shelfStuff[^1].Label = "XP";
        shelfStuff.Add(Box(PropKind.Book, 1.85f, 2.4f, 0.1f, 0.32f, 0.4f, new SKColor(0x4A, 0x3A, 0x8A), _palette.Sample(0.5f), RattleMode.Rock));
        shelfStuff.Add(Box(PropKind.Book, 1.98f, 2.4f, 0.1f, 0.36f, 0.4f, new SKColor(0x2A, 0x6A, 0x8A), _palette.Sample(0.5f), RattleMode.Rock));

        var poster = Hang(PropKind.Frame, new Vector2(4.0f, 1.12f), 0.62f, 0.48f, 1f, new Vector2(4.0f, 0.62f), new SKColor(0x18, 0x18, 0x22), _palette.Sample(0.7f));
        var ring = Hang(PropKind.Pendant, new Vector2(3.25f, 1.2f), 0.6f, 0.3f, 2f, new Vector2(3.25f, 0.1f), new SKColor(0x2A, 0x2A, 0x36), _palette.Sample(0.4f));

        Light(monitor, new Vector2(0, -0.1f), _palette.Sample(0.5f), 0.9f, LightKind.Bass);
        Light(ring, new Vector2(0, 0.2f), _palette.Sample(0.2f), 0.8f, LightKind.Bass);

        Disaster(1, () => Drop(poster, -0.1f));
        Disaster(1, () => Slide([cans[0]], -1, 1.6f));
        Disaster(2, () => Slide([phones, cans[1]], -1, 2.2f));
        Disaster(3, () => Snap(shelf, shelfStuff));
        Disaster(3, () => { Slide([monitor], -1, 1.4f); Topple(monitor, -1, 3.6f); });
        Disaster(4, () => Drop(ring));
    }

    private void Cabin()
    {
        Walls(new SKColor(0x8A, 0x58, 0x34), new SKColor(0x6E, 0x44, 0x28), WallStyle.Logs, new SKColor(0x4E, 0x30, 0x1C));
        Floor(new SKColor(0x6A, 0x46, 0x2C), FloorStyle.Planks);
        Rug(2.3f, 5.6f, new SKColor(0x8E, 0x2E, 0x2A), new SKColor(0xE8, 0xD8, 0xB8));
        Window(new SKRect(0.9f, 0.8f, 2.55f, 2.5f), Outside.Aurora);
        // ceiling beam + firewood stack
        _wallArt.Add(c =>
        {
            Rect(c, new SKColor(0x4A, 0x2E, 0x1A), -4, 0, 12, 0.22f);
            for (int i = 0; i < 9; i++)
            {
                float x = 0.15f + (i % 3) * 0.24f + (i / 3 % 2) * 0.12f, y = FloorY - 0.12f - (i / 3) * 0.2f;
                _fill.Color = new SKColor(0x7A, 0x4E, 0x2C);
                c.DrawCircle(x, y, 0.11f, _fill);
                _fill.Color = new SKColor(0xD8, 0xB0, 0x78);
                c.DrawCircle(x, y, 0.07f, _fill);
            }
        });

        var shelf = Shelf(5.2f, 7.2f, 1.9f, new SKColor(0x5A, 0x36, 0x1E), snapRight: false);
        var items = new List<Prop>
        {
            Box(PropKind.Mug, 5.45f, 1.9f, 0.2f, 0.18f, 0.4f, new SKColor(0xC8, 0x3A, 0x2E), new SKColor(0xF0, 0xE6, 0xD0), RattleMode.Hop),
            Box(PropKind.Jar, 5.85f, 1.9f, 0.18f, 0.28f, 0.5f, new SKColor(0xD8, 0x9A, 0x3A), new SKColor(0x6A, 0x4A, 0x2E), RattleMode.Rock),
            Box(PropKind.Lantern, 6.3f, 1.9f, 0.26f, 0.42f, 1f, new SKColor(0x2E, 0x2A, 0x26), new SKColor(0xFF, 0xC8, 0x6A), RattleMode.Rock),
            Box(PropKind.Book, 6.75f, 1.9f, 0.11f, 0.36f, 0.5f, new SKColor(0x3E, 0x5A, 0x3A), new SKColor(0xE8, 0xD8, 0xA8), RattleMode.Rock),
            Box(PropKind.Book, 6.88f, 1.9f, 0.11f, 0.4f, 0.5f, new SKColor(0x8A, 0x2E, 0x2A), new SKColor(0xE8, 0xD8, 0xA8), RattleMode.Rock),
        };
        var frame = Hang(PropKind.Frame, new Vector2(2.95f, 1.35f), 0.4f, 0.52f, 1f, new Vector2(2.95f, 0.9f), new SKColor(0x5A, 0x3A, 0x22), new SKColor(0x6A, 0x9A, 0xC8));
        var skis = Hang(PropKind.Skis, new Vector2(7.55f, 2.25f), 0.26f, 1.7f, 2f, new Vector2(7.55f, 1.25f), new SKColor(0xC8, 0x2E, 0x2E), new SKColor(0x2E, 0x6A, 0xC8));
        var chandelier = Hang(PropKind.Antlers, new Vector2(4.1f, 1.15f), 1.15f, 0.5f, 3f, new Vector2(4.1f, 0.22f), new SKColor(0xE8, 0xDC, 0xC0), new SKColor(0xFF, 0xC8, 0x6A));

        Light(items[2], new Vector2(0, 0), new SKColor(0xFF, 0xC0, 0x60), 0.6f, LightKind.Bass);
        foreach (float ox in new[] { -0.45f, 0f, 0.45f })
            Light(chandelier, new Vector2(ox, -0.2f), new SKColor(0xFF, 0xD0, 0x80), 0.45f, LightKind.Steady, bulb: 0.035f);

        Disaster(1, () => Drop(frame, 0.15f));
        Disaster(1, () => Drop(skis, -0.6f));
        Disaster(2, () => Slide(items, -1, 2.3f));
        Disaster(3, () => Snap(shelf, items));
        Disaster(4, () => Drop(chandelier));
    }

    private void Kitchen()
    {
        Walls(new SKColor(0xF6, 0xE8, 0xD2), new SKColor(0xEC, 0xD8, 0xBC), WallStyle.Plain, new SKColor(0xE6, 0xD0, 0xB0));
        Backsplash(2.25f, 3.15f, _palette.Sample(0.4f));
        Floor(new SKColor(0x3A, 0x34, 0x34), FloorStyle.Checker);
        Window(new SKRect(0.55f, 0.75f, 2.0f, 2.05f), Outside.Sunny);

        // counters: left with the stove, right with cabinets; upper cabinets top right
        var wood = new SKColor(0xB8, 0x7A, 0x4A);
        Furniture(new SKRect(-0.2f, 0.6f, 8.2f, FloorY), c =>
        {
            CounterBlock(c, -0.2f, 2.7f, 3.15f, wood);
            CounterBlock(c, 4.95f, 8.2f, 3.15f, wood);
            // stove
            Rect(c, new SKColor(0x2E, 0x2E, 0x34), 0.4f, 3.12f, 1.5f, 3.18f);
            foreach (float sx in new[] { 0.65f, 1.25f })
            {
                _fill.Color = new SKColor(0x18, 0x18, 0x1C);
                c.DrawOval(sx, 3.11f, 0.2f, 0.03f, _fill);
            }
            RoundRect(c, new SKColor(0x3A, 0x3A, 0x42), 0.4f, 3.4f, 1.5f, 4.05f, 0.04f);    // oven door
            RoundRect(c, new SKColor(0x24, 0x22, 0x28), 0.55f, 3.55f, 1.35f, 3.85f, 0.03f);
            // upper cabinets
            RoundRect(c, PropArt.Shade(wood, 1.05f), 6.6f, 0.6f, 8.2f, 1.75f, 0.03f);
            Rect(c, PropArt.Shade(wood, 0.7f), 7.38f, 0.65f, 7.42f, 1.7f);
            // pot rail over the dancer
            Rect(c, new SKColor(0x6A, 0x6A, 0x72), 3.2f, 1.08f, 4.85f, 1.12f);
        });
        Surface(-0.2f, 2.7f, 3.15f);
        Surface(4.95f, 8.2f, 3.15f);

        var flour = Box(PropKind.FlourBag, 2.0f, 3.15f, 0.36f, 0.48f, 1.5f, new SKColor(0xEE, 0xE4, 0xCC), _palette.Sample(0.4f), RattleMode.Rock);
        var jar = Box(PropKind.Jar, 2.45f, 3.15f, 0.16f, 0.26f, 0.5f, new SKColor(0xC8, 0x6A, 0x2A), new SKColor(0x8A, 0x3A, 0x22), RattleMode.Rock);
        var bowl = Box(PropKind.Bowl, 5.7f, 3.15f, 0.5f, 0.16f, 1f, new SKColor(0x4A, 0x8A, 0xB8), new SKColor(0x3A, 0x6A, 0x8A), RattleMode.None);
        var fruit = new[]
        {
            Ball(PropKind.Fruit, 5.55f, 3.0f, 0.075f, 0.2f, new SKColor(0xF0, 0x8A, 0x1E), new SKColor(0x4A, 0x6A, 0x22)),
            Ball(PropKind.Fruit, 5.72f, 3.0f, 0.075f, 0.2f, new SKColor(0xD8, 0x24, 0x2A), new SKColor(0x4A, 0x6A, 0x22)),
            Ball(PropKind.Fruit, 5.89f, 3.0f, 0.07f, 0.2f, new SKColor(0xE8, 0xD0, 0x30), new SKColor(0x4A, 0x6A, 0x22)),
        };
        var plates = Box(PropKind.Plates, 7.3f, 3.15f, 0.42f, 0.3f, 2f, new SKColor(0xF4, 0xF2, 0xEC), _palette.Sample(0.6f), RattleMode.Rock);

        var shelf = Shelf(5.15f, 6.45f, 1.65f, PropArt.Shade(wood, 0.8f), snapRight: false);
        var jars = new List<Prop>();
        SKColor[] spice = [new(0xC8, 0x3A, 0x22), new(0xD8, 0xA0, 0x2A), new(0x6A, 0x8A, 0x2E), new(0x8A, 0x4A, 0x22)];
        for (int i = 0; i < 4; i++)
            jars.Add(Box(PropKind.Jar, 5.35f + i * 0.28f, 1.65f, 0.14f, 0.22f, 0.3f, spice[i], new SKColor(0x2E, 0x2A, 0x28), RattleMode.Rock));

        var pans = new[]
        {
            Hang(PropKind.Pan, new Vector2(3.55f, 1.32f), 0.36f, 0.34f, 1.5f, new Vector2(3.55f, 1.1f), new SKColor(0x9A, 0x9C, 0xA4), wood),
            Hang(PropKind.Pan, new Vector2(4.05f, 1.35f), 0.42f, 0.36f, 1.8f, new Vector2(4.05f, 1.1f), new SKColor(0xC8, 0x6A, 0x3A), wood),
            Hang(PropKind.Pan, new Vector2(4.55f, 1.32f), 0.36f, 0.34f, 1.5f, new Vector2(4.55f, 1.1f), new SKColor(0x9A, 0x9C, 0xA4), wood),
        };
        _lights.Add(new RoomLight { Pos = new Vector2(1.0f, 3.0f), Color = new SKColor(0xFF, 0x7A, 0x2A), Kind = LightKind.Bass, GlowRadius = 0.5f, GlowAlpha = 120 });

        Disaster(1, () => Drop(pans[1], 0.05f));
        Disaster(1, () => Kick(fruit[0], -1.4f));
        Disaster(2, () => Slide([flour, jar], 1, 2.4f));
        Disaster(2, () => Slide([fruit[1], fruit[2]], -1, 1.6f));
        Disaster(3, () => Snap(shelf, jars));
        Disaster(3, () => Slide([plates, bowl], -1, 3.0f));
        Disaster(4, () => { Drop(pans[0], 0.3f); Drop(pans[2], -0.3f); });
    }

    private void Office()
    {
        Walls(new SKColor(0xDE, 0xE6, 0xEE), new SKColor(0xC8, 0xD4, 0xE0), WallStyle.Plain, new SKColor(0xB8, 0xC6, 0xD4));
        _wallArt.Add(c => Rect(c, new SKColor(0xA8, 0xB6, 0xC6), -4, 2.62f, 12, 2.68f)); // chair rail
        Floor(new SKColor(0x5A, 0x64, 0x7A), FloorStyle.Carpet);
        Window(new SKRect(5.4f, 0.7f, 7.5f, 2.35f), Outside.SnowNight);

        Furniture(new SKRect(0.2f, 2.9f, 7.6f, FloorY), c =>
        {
            Rect(c, new SKColor(0xD8, 0xC8, 0xA8), 0.25f, 2.95f, 2.75f, 3.03f);              // desk
            Rect(c, new SKColor(0x8A, 0x92, 0xA0), 0.35f, 3.03f, 0.43f, FloorY);
            Rect(c, new SKColor(0x8A, 0x92, 0xA0), 2.57f, 3.03f, 2.65f, FloorY);
            RoundRect(c, new SKColor(0x7A, 0x84, 0x96), 1.7f, 3.03f, 2.55f, 3.8f, 0.03f);    // drawer unit
            // filing cabinet
            RoundRect(c, new SKColor(0x8C, 0x96, 0xA6), 6.0f, 2.7f, 6.85f, FloorY, 0.03f);
            for (int i = 0; i < 3; i++)
            {
                RoundRect(c, new SKColor(0x9C, 0xA6, 0xB6), 6.06f, 2.78f + i * 0.47f, 6.79f, 3.18f + i * 0.47f, 0.02f);
                Rect(c, new SKColor(0x5A, 0x62, 0x70), 6.33f, 2.9f + i * 0.47f, 6.52f, 2.93f + i * 0.47f);
            }
        });
        Surface(0.25f, 2.75f, 2.95f);
        Surface(6.0f, 6.85f, 2.7f);

        var monitor = Box(PropKind.Monitor, 1.25f, 2.95f, 0.85f, 0.62f, 3f, new SKColor(0x1C, 0x1C, 0x22), _palette.Sample(0.5f), RattleMode.None);
        var mug = Box(PropKind.Mug, 2.1f, 2.95f, 0.17f, 0.17f, 0.3f, _palette.Sample(0.8f), new SKColor(0xF4, 0xF4, 0xF4), RattleMode.Creep);
        mug.CreepDir = 1;
        var paper = Box(PropKind.Binder, 2.45f, 2.95f, 0.12f, 0.34f, 0.4f, new SKColor(0x3A, 0x6A, 0xC8), new SKColor(0xF4, 0xF4, 0xF4), RattleMode.Rock);
        var plant = Box(PropKind.Plant, 6.42f, 2.7f, 0.42f, 0.72f, 1.5f, new SKColor(0xE8, 0xE8, 0xEC), _palette.Sample(0.5f), RattleMode.Rock);

        var shelf = Shelf(0.35f, 2.45f, 1.75f, new SKColor(0x9A, 0xA4, 0xB4), snapRight: true);
        var binders = new List<Prop>();
        SKColor[] bc = [new(0x3A, 0x6A, 0xC8), new(0xC8, 0x3A, 0x3A), new(0x3A, 0x9A, 0x5A), new(0xE8, 0xA8, 0x2E), new(0x6A, 0x4A, 0xA8)];
        for (int i = 0; i < 5; i++)
            binders.Add(Box(PropKind.Binder, 0.7f + i * 0.17f, 1.75f, 0.13f, 0.4f, 0.6f, bc[i], new SKColor(0xF4, 0xF4, 0xF4), RattleMode.Rock));
        var clock = Hang(PropKind.Clock, new Vector2(4.0f, 0.95f), 0.5f, 0.5f, 1f, new Vector2(4.0f, 0.6f), new SKColor(0x3A, 0x3E, 0x48), new SKColor(0xD8, 0x2A, 0x2A));
        var tube = Hang(PropKind.Pendant, new Vector2(4.25f, 1.55f), 0.7f, 0.26f, 2.5f, new Vector2(4.25f, 0f), new SKColor(0xC8, 0xD0, 0xDC), _palette.Sample(0.3f));

        Light(tube, new Vector2(0, 0.15f), new SKColor(0xE8, 0xF4, 0xFF), 0.9f, LightKind.Steady);
        Light(monitor, new Vector2(0, -0.1f), _palette.Sample(0.5f), 0.7f, LightKind.Bass);

        Disaster(1, () => Drop(clock, 0.1f));
        Disaster(1, () => Kick(mug, 1.4f));
        Disaster(2, () => Slide([paper], 1, 2f));
        Disaster(2, () => Topple(plant, -1, 2f));
        Disaster(3, () => Snap(shelf, binders));
        Disaster(3, () => { Slide([monitor], 1, 1.4f); Topple(monitor, 1, 3.6f); });
        Disaster(4, () => Drop(tube));
    }

    private void ChristmasRoom()
    {
        bool candy = ThemeName == "Candy Cane";
        Walls(candy ? new SKColor(0xFC, 0xF0, 0xF0) : new SKColor(0x8E, 0x2C, 0x2C),
              candy ? new SKColor(0xF2, 0xE0, 0xE2) : new SKColor(0x6E, 0x1E, 0x22),
              candy ? WallStyle.Stripes : WallStyle.Damask,
              candy ? new SKColor(0xF4, 0xC8, 0xCE) : new SKColor(0xA2, 0x3A, 0x36));
        Floor(new SKColor(0x7A, 0x4E, 0x30), FloorStyle.Planks);
        Rug(2.6f, 5.6f, new SKColor(0x2E, 0x6A, 0x3E), new SKColor(0xE8, 0xD8, 0xB0));
        Window(new SKRect(0.35f, 0.85f, 1.7f, 2.45f), Outside.SnowNight);

        // fireplace with a mantel; its fire glows with the bass
        Furniture(new SKRect(1.85f, 2.4f, 3.1f, FloorY), c =>
        {
            RoundRect(c, new SKColor(0x9A, 0x4A, 0x3A), 1.9f, 2.55f, 3.05f, FloorY, 0.02f);
            for (int row = 0; row < 8; row++)
                for (int col = 0; col < 5; col++)
                {
                    float x = 1.92f + col * 0.23f + (row % 2) * 0.11f, y = 2.58f + row * 0.2f;
                    if (x > 3.02f) continue;
                    RoundRect(c, new SKColor(0xB0, 0x5A, 0x44), x, y, MathF.Min(x + 0.21f, 3.03f), y + 0.18f, 0.01f);
                }
            RoundRect(c, new SKColor(0x1E, 0x12, 0x10), 2.12f, 3.2f, 2.83f, FloorY, 0.18f);  // firebox
            Rect(c, new SKColor(0x5A, 0x38, 0x22), 1.8f, 2.45f, 3.15f, 2.57f);                 // mantel
        });
        Surface(1.8f, 3.15f, 2.45f);
        _animatedBack.Add((c, glow, time, f) =>
        {
            // little fire in the firebox
            float k = 0.6f + 0.4f * MathF.Min(1f, f.Bass * 1.4f + f.Pulse * 0.5f);
            for (int i = 0; i < 3; i++)
            {
                float x = 2.3f + i * 0.17f, hgt = (0.22f + 0.08f * MathF.Sin(time * 9 + i * 2)) * k;
                _fill.Color = new SKColor(0xFF, 0x8A, 0x2A);
                c.DrawOval(x, FloorY - 0.06f - hgt / 2, 0.08f, hgt / 2, _fill);
                _fill.Color = new SKColor(0xFF, 0xE0, 0x80);
                c.DrawOval(x, FloorY - 0.05f - hgt / 4, 0.04f, hgt / 4, _fill);
            }
            if (glow != null)
            {
                _fill.Color = new SKColor(0xFF, 0x90, 0x40, (byte)(120 * k));
                glow.DrawCircle(2.47f, FloorY - 0.3f, 0.7f, _fill);
            }
        });

        var red = candy ? new SKColor(0xE8, 0x2A, 0x44) : new SKColor(0xC8, 0x1E, 0x2A);
        var green = new SKColor(0x2E, 0x8A, 0x4A);
        var stockings = new[]
        {
            Hang(PropKind.Stocking, new Vector2(2.2f, 2.86f), 0.3f, 0.48f, 0.5f, new Vector2(2.2f, 2.58f), red, green),
            Hang(PropKind.Stocking, new Vector2(2.8f, 2.86f), 0.3f, 0.48f, 0.5f, new Vector2(2.8f, 2.58f), green, red),
        };
        var candles = new[]
        {
            Box(PropKind.Candle, 2.0f, 2.45f, 0.08f, 0.3f, 0.3f, new SKColor(0xF4, 0xEC, 0xD8), red, RattleMode.Rock),
            Box(PropKind.Candle, 2.95f, 2.45f, 0.08f, 0.24f, 0.3f, new SKColor(0xF4, 0xEC, 0xD8), red, RattleMode.Rock),
        };
        var mantelOrn = Ball(PropKind.Ornament, 2.5f, 2.45f, 0.08f, 0.2f, _palette.Sample(0.3f), new SKColor(0xF4, 0xE0, 0x90), RattleMode.Hop);

        var shelf = Shelf(1.95f, 3.05f, 1.55f, new SKColor(0x5A, 0x38, 0x22), snapRight: true);
        var shelfOrns = new List<Prop>
        {
            Ball(PropKind.Ornament, 2.2f, 1.55f, 0.09f, 0.2f, _palette.Sample(0f), new SKColor(0xF4, 0xE0, 0x90), RattleMode.Hop),
            Box(PropKind.Present, 2.55f, 1.55f, 0.26f, 0.22f, 0.4f, green, new SKColor(0xE8, 0xC8, 0x50), RattleMode.Rock),
            Ball(PropKind.Ornament, 2.85f, 1.55f, 0.08f, 0.2f, _palette.Sample(0.7f), new SKColor(0xF4, 0xE0, 0x90), RattleMode.Hop),
        };

        var tree = Box(PropKind.Tree, 6.75f, FloorY, 1.45f, 2.7f, 8f, green, red, RattleMode.None);
        var presents = new[]
        {
            Box(PropKind.Present, 5.3f, FloorY, 0.44f, 0.36f, 1f, red, new SKColor(0xE8, 0xC8, 0x50), RattleMode.Rock),
            Box(PropKind.Present, 5.72f, FloorY, 0.34f, 0.28f, 0.8f, _palette.Sample(0.5f), new SKColor(0xF4, 0xF4, 0xF4), RattleMode.Rock),
            Box(PropKind.Present, 7.75f, FloorY, 0.36f, 0.42f, 1f, new SKColor(0x3A, 0x5A, 0xA8), new SKColor(0xE8, 0xC8, 0x50), RattleMode.Rock),
        };
        var wreath = Hang(PropKind.Wreath, new Vector2(4.0f, 1.08f), 0.62f, 0.62f, 1.2f, new Vector2(4.0f, 0.66f), green, red);

        StringLights(0.0f, 8.0f, 0.18f, 0.42f, 22);
        foreach (var cdl in candles) Light(cdl, new Vector2(0, -0.17f), new SKColor(0xFF, 0xC0, 0x60), 0.35f, LightKind.Steady);

        Disaster(1, () => Drop(stockings[0], 0.3f));
        Disaster(1, () => Drop(wreath, 0.05f));
        Disaster(2, () => Slide([.. candles, mantelOrn], 1, 1.8f));
        Disaster(2, () => Slide(presents.Take(2), -1, 1.8f));
        Disaster(3, () => Snap(shelf, shelfOrns));
        Disaster(3, () => Drop(stockings[1], 0.4f));
        Disaster(4, () => Topple(tree, -1, 4.6f));
    }

    private void Playroom()
    {
        Walls(new SKColor(0xFF, 0xF2, 0xD8), new SKColor(0xFB, 0xE4, 0xC4), WallStyle.Dots, new SKColor(0xF4, 0xC8, 0x9A));
        Floor(new SKColor(0x8A, 0xB8, 0xD8), FloorStyle.Carpet);
        Rug(2.3f, 5.7f, _palette.Sample(0.1f), _palette.Sample(0.6f));
        Window(new SKRect(0.5f, 0.75f, 2.0f, 2.05f), Outside.Sunny);

        // toy chest
        Furniture(new SKRect(6.5f, 3.5f, 8.0f, FloorY), c =>
        {
            RoundRect(c, new SKColor(0xE8, 0x8A, 0x4A), 6.55f, 3.6f, 7.95f, FloorY, 0.05f);
            RoundRect(c, new SKColor(0xD8, 0x6A, 0x3A), 6.5f, 3.52f, 8.0f, 3.66f, 0.04f);
            RoundRect(c, new SKColor(0xFF, 0xD8, 0x6A), 7.1f, 3.8f, 7.4f, 3.95f, 0.03f);
        });
        Surface(6.5f, 8.0f, 3.52f);

        var shelf = Shelf(0.4f, 2.3f, 2.35f, new SKColor(0xF4, 0xF0, 0xE8), snapRight: true);
        var bear = Box(PropKind.Bear, 0.8f, 2.35f, 0.42f, 0.52f, 1f, new SKColor(0xB8, 0x7A, 0x4A), _palette.Sample(0f), RattleMode.Rock);
        var car = Box(PropKind.Toy, 1.4f, 2.35f, 0.42f, 0.22f, 0.6f, _palette.Sample(0.75f), new SKColor(0x22, 0x22, 0x28), RattleMode.Creep);
        car.CreepDir = 1;
        var smallBlocks = new[]
        {
            Box(PropKind.Block, 1.85f, 2.35f, 0.2f, 0.2f, 0.3f, _palette.Sample(0.3f), _palette.Sample(0.3f), RattleMode.Rock),
            Box(PropKind.Block, 2.1f, 2.35f, 0.2f, 0.2f, 0.3f, _palette.Sample(0.55f), _palette.Sample(0.55f), RattleMode.Rock),
        };
        smallBlocks[0].Label = "1";
        smallBlocks[1].Label = "2";

        // the tower
        var tower = new List<Prop>();
        string letters = "ABCDE";
        for (int i = 0; i < 5; i++)
        {
            var blk = Box(PropKind.Block, 5.65f + (i % 2) * 0.02f, FloorY - i * 0.31f, 0.3f, 0.3f, 0.6f, _palette.Sample(i / 4f), _palette.Sample(i / 4f), RattleMode.None);
            blk.Label = letters[i].ToString();
            tower.Add(blk);
        }
        var balls = new[]
        {
            Ball(PropKind.Ball, 6.95f, 3.52f, 0.17f, 0.4f, new SKColor(0xE8, 0x3A, 0x3A), new SKColor(0xF4, 0xF4, 0xF4), RattleMode.None),
            Ball(PropKind.Ball, 7.55f, 3.52f, 0.13f, 0.3f, new SKColor(0x3A, 0x8A, 0xE8), new SKColor(0xFF, 0xD8, 0x4A), RattleMode.None),
        };
        var mobile = Hang(PropKind.Mobile, new Vector2(4.0f, 1.0f), 0.85f, 0.5f, 1f, new Vector2(4.0f, 0.3f), new SKColor(0x8A, 0x6A, 0x4A), _palette.Sample(0.5f));

        Disaster(1, () => Kick(balls[0], -2.6f, -2.6f));
        Disaster(1, () => Kick(car, 1.6f));
        Disaster(2, () => Slide([bear, .. smallBlocks], 1, 2.2f));
        Disaster(3, () =>
        {
            tower[^1].Body.Push(new Vector2(-3.2f, -1f), StepDt);
            tower[^2].Body.Push(new Vector2(-2.6f, -0.6f), StepDt);
            tower[^3].Body.Push(new Vector2(-1.8f, -0.4f), StepDt);
        });
        Disaster(3, () => Snap(shelf, [bear, car, .. smallBlocks]));
        Disaster(4, () => { Drop(mobile); Kick(balls[1], -3f, -2.8f); });
    }

    // ================================================================== art helpers

    private void Light(Prop owner, Vector2 offset, SKColor color, float glowRadius, LightKind kind, float bulb = 0f)
        => _lights.Add(new RoomLight { Owner = owner, Offset = offset, Color = color, GlowRadius = glowRadius * 0.7f, GlowAlpha = 105, Kind = kind, BulbRadius = bulb, Seed = _rng.Next(1000), BehindProps = false });

    private void Rect(SKCanvas c, SKColor col, float x0, float y0, float x1, float y1)
    {
        _fill.Shader = null;
        _fill.Color = col;
        c.DrawRect(new SKRect(x0, y0, x1, y1), _fill);
    }

    private void RoundRect(SKCanvas c, SKColor col, float x0, float y0, float x1, float y1, float r)
    {
        _fill.Shader = null;
        _fill.Color = col;
        c.DrawRoundRect(new SKRect(x0, y0, x1, y1), r, r, _fill);
    }

    private void Walls(SKColor top, SKColor bottom, WallStyle style, SKColor pattern)
    {
        _wallArt.Add(c =>
        {
            // ceiling above the room (for tall screens)
            Rect(c, PropArt.Shade(bottom, 0.75f), -4, -2, 12, 0);
            using (var g = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, FloorY), new[] { top, bottom }, null, SKShaderTileMode.Clamp))
            {
                _fill.Shader = g;
                _fill.Color = SKColors.White;
                c.DrawRect(-4, 0, 16, FloorY, _fill);
                _fill.Shader = null;
            }
            switch (style)
            {
                case WallStyle.Stripes:
                    for (float x = -4; x < 12; x += 0.5f) Rect(c, pattern.WithAlpha(110), x, 0, x + 0.22f, FloorY);
                    break;
                case WallStyle.Logs:
                    for (float y = 0.22f; y < FloorY; y += 0.3f)
                    {
                        Rect(c, pattern, -4, y, 12, y + 0.04f);                                  // chinking gap
                        Rect(c, PropArt.Shade(top, 1.08f), -4, y + 0.06f, 12, y + 0.11f);        // highlight
                        Rect(c, PropArt.Shade(bottom, 0.85f), -4, y + 0.24f, 12, y + 0.3f);      // underside shadow
                    }
                    var rng = new Random(3);
                    for (int i = 0; i < 40; i++)
                    {
                        float x = (float)rng.NextDouble() * 16 - 4, y = 0.3f + (int)(rng.NextDouble() * 13) * 0.3f + 0.12f;
                        _fill.Color = pattern.WithAlpha(90);
                        c.DrawOval(x, y, 0.05f, 0.03f, _fill);                                    // knots
                    }
                    break;
                case WallStyle.Dots:
                {
                    SKColor[] dots = [_palette.Sample(0f), _palette.Sample(0.33f), _palette.Sample(0.66f), _palette.Sample(1f)];
                    int k = 0;
                    for (float y = 0.3f; y < FloorY - 0.2f; y += 0.45f)
                        for (float x = -4 + (k % 2) * 0.22f; x < 12; x += 0.45f)
                        {
                            _fill.Color = dots[(k++) % 4].WithAlpha(70);
                            c.DrawCircle(x, y, 0.05f, _fill);
                        }
                    break;
                }
                case WallStyle.Damask:
                    for (float y = 0.3f; y < FloorY; y += 0.6f)
                        for (float x = -4 + ((int)(y / 0.6f) % 2) * 0.35f; x < 12; x += 0.7f)
                        {
                            _fill.Color = pattern.WithAlpha(150);
                            c.DrawOval(x, y, 0.07f, 0.12f, _fill);
                            c.DrawOval(x, y, 0.13f, 0.04f, _fill);
                        }
                    break;
            }
            // baseboard + crown
            Rect(c, PropArt.Shade(bottom, 0.6f), -4, FloorY - 0.12f, 12, FloorY);
            Rect(c, PropArt.Shade(bottom, 1.1f), -4, FloorY - 0.13f, 12, FloorY - 0.11f);
            Rect(c, PropArt.Shade(top, 0.85f), -4, 0, 12, 0.06f);
        });
    }

    private void Floor(SKColor col, FloorStyle style)
    {
        _wallArt.Add(c =>
        {
            Rect(c, col, -4, FloorY, 12, 7);
            switch (style)
            {
                case FloorStyle.Planks:
                    for (float y = FloorY + 0.09f; y < 7; y += 0.14f + (y - FloorY) * 0.12f)
                        Rect(c, PropArt.Shade(col, 0.82f), -4, y, 12, y + 0.012f);
                    break;
                case FloorStyle.Checker:
                {
                    int row = 0;
                    for (float y = FloorY; y < 7; y += 0.18f, row++)
                        for (float x = -4 + (row % 2) * 0.3f; x < 12; x += 0.6f)
                            Rect(c, new SKColor(0xEC, 0xE6, 0xDA), x, y, x + 0.3f, y + 0.18f);
                    break;
                }
                case FloorStyle.Carpet:
                    for (float y = FloorY + 0.05f; y < 7; y += 0.11f)
                        Rect(c, PropArt.Shade(col, 0.92f), -4, y, 12, y + 0.03f);
                    break;
            }
            Rect(c, new SKColor(0, 0, 0, 50), -4, FloorY, 12, FloorY + 0.05f); // contact shadow
        });
    }

    private void Rug(float x0, float x1, SKColor a, SKColor b)
    {
        _wallArt.Add(c =>
        {
            RoundRect(c, a, x0, FloorY + 0.03f, x1, FloorY + 0.22f, 0.05f);
            RoundRect(c, b, x0 + 0.12f, FloorY + 0.07f, x1 - 0.12f, FloorY + 0.18f, 0.04f);
            RoundRect(c, a, x0 + 0.3f, FloorY + 0.1f, x1 - 0.3f, FloorY + 0.15f, 0.02f);
        });
    }

    private void Window(SKRect r, Outside outside)
    {
        var panes = new[]
        {
            new SKRect(r.Left + 0.08f, r.Top + 0.08f, r.MidX - 0.03f, r.MidY - 0.03f),
            new SKRect(r.MidX + 0.03f, r.Top + 0.08f, r.Right - 0.08f, r.MidY - 0.03f),
            new SKRect(r.Left + 0.08f, r.MidY + 0.03f, r.MidX - 0.03f, r.Bottom - 0.08f),
            new SKRect(r.MidX + 0.03f, r.MidY + 0.03f, r.Right - 0.08f, r.Bottom - 0.08f),
        };
        var glass = new SKRect(r.Left + 0.08f, r.Top + 0.08f, r.Right - 0.08f, r.Bottom - 0.08f);
        SKColor[] sky = outside switch
        {
            Outside.Sunset => [_palette.Sample(1f), _palette.Sample(0.5f), _palette.Sample(0.1f)],
            Outside.Aurora => [new(0x02, 0x06, 0x14), new(0x08, 0x18, 0x30)],
            Outside.SnowNight => [new(0x06, 0x0C, 0x22), new(0x1C, 0x2E, 0x58)],
            Outside.City => [new(0x0A, 0x06, 0x1C), new(0x2A, 0x10, 0x4A)],
            _ => [new(0x5A, 0xB4, 0xF4), new(0xC8, 0xEA, 0xFF)],
        };
        var frame = new SKColor(0xF4, 0xEE, 0xE2);
        _wallArt.Add(c =>
        {
            RoundRect(c, PropArt.Shade(frame, 0.8f), r.Left - 0.04f, r.Top - 0.04f, r.Right + 0.04f, r.Bottom + 0.12f, 0.03f);
            using (var g = SKShader.CreateLinearGradient(new SKPoint(0, glass.Top), new SKPoint(0, glass.Bottom), sky, null, SKShaderTileMode.Clamp))
            {
                _fill.Shader = g;
                _fill.Color = SKColors.White;
                c.DrawRect(glass, _fill);
                _fill.Shader = null;
            }
            c.Save();
            c.ClipRect(glass);
            var rng = new Random(9);
            switch (outside)
            {
                case Outside.Sunset:
                    _fill.Color = new SKColor(0xFF, 0xE6, 0x9A);
                    c.DrawCircle(glass.MidX + 0.15f, glass.Bottom - 0.35f, 0.25f, _fill);
                    Hills(c, glass, PropArt.Shade(_palette.Sample(0.1f), 0.5f));
                    break;
                case Outside.Sunny:
                    _fill.Color = new SKColor(0xFF, 0xF2, 0x9A);
                    c.DrawCircle(glass.Right - 0.3f, glass.Top + 0.3f, 0.16f, _fill);
                    _fill.Color = SKColors.White.WithAlpha(220);
                    c.DrawOval(glass.Left + 0.45f, glass.Top + 0.45f, 0.25f, 0.08f, _fill);
                    c.DrawCircle(glass.Left + 0.38f, glass.Top + 0.38f, 0.1f, _fill);
                    Hills(c, glass, new SKColor(0x5C, 0xB0, 0x4C));
                    break;
                default:
                    for (int i = 0; i < 30; i++)
                    {
                        _fill.Color = SKColors.White.WithAlpha((byte)(120 + rng.Next(130)));
                        c.DrawCircle(glass.Left + (float)rng.NextDouble() * glass.Width, glass.Top + (float)rng.NextDouble() * glass.Height * 0.6f, 0.008f + 0.008f * (float)rng.NextDouble(), _fill);
                    }
                    if (outside == Outside.SnowNight)
                    {
                        // snowy ground and pines outside
                        _fill.Color = new SKColor(0xD8, 0xE4, 0xF4);
                        c.DrawRect(glass.Left, glass.Bottom - 0.25f, glass.Width, 0.25f, _fill);
                        for (int i = 0; i < 4; i++) Pine(c, glass.Left + 0.2f + i * glass.Width / 4, glass.Bottom - 0.2f, 0.45f + 0.1f * (i % 2));
                    }
                    else if (outside == Outside.Aurora)
                    {
                        Hills(c, glass, new SKColor(0x0A, 0x16, 0x22));
                        for (int i = 0; i < 4; i++) Pine(c, glass.Left + 0.15f + i * glass.Width / 4, glass.Bottom, 0.5f);
                    }
                    break;
            }
            c.Restore();
            // muntins + sill
            Rect(c, frame, glass.MidX - 0.03f, glass.Top, glass.MidX + 0.03f, glass.Bottom);
            Rect(c, frame, glass.Left, glass.MidY - 0.03f, glass.Right, glass.MidY + 0.03f);
            Line(c, frame, 0.08f, glass);
            RoundRect(c, PropArt.Shade(frame, 0.92f), r.Left - 0.12f, r.Bottom + 0.02f, r.Right + 0.12f, r.Bottom + 0.12f, 0.02f);
        });

        if (outside == Outside.Aurora || outside == Outside.SnowNight)
        {
            _animatedBack.Add((c, glow, time, f) =>
            {
                c.Save();
                c.ClipRect(glass);
                if (outside == Outside.Aurora)
                {
                    // shimmering aurora ribbons behind the window
                    for (int k = 0; k < 2; k++)
                    {
                        using var path = new SKPath();
                        float baseY = glass.Top + glass.Height * (0.2f + 0.15f * k);
                        path.MoveTo(glass.Left - 0.1f, baseY);
                        for (float x = glass.Left; x <= glass.Right + 0.1f; x += 0.08f)
                            path.LineTo(x, baseY + 0.08f * MathF.Sin(x * 3f + time * (0.6f + 0.3f * k) + k));
                        for (float x = glass.Right + 0.1f; x >= glass.Left - 0.1f; x -= 0.08f)
                            path.LineTo(x, baseY + 0.32f + 0.05f * MathF.Sin(x * 2f + time * 0.4f));
                        path.Close();
                        var col = _palette.Sample(k * 0.6f);
                        byte a = (byte)(90 + 90 * f.Mid);
                        using var g = SKShader.CreateLinearGradient(new SKPoint(0, baseY - 0.1f), new SKPoint(0, baseY + 0.4f),
                            new[] { col.WithAlpha(0), col.WithAlpha(a), col.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
                        _fill.Shader = g;
                        _fill.Color = SKColors.White;
                        c.DrawPath(path, _fill);
                        _fill.Shader = null;
                    }
                }
                else
                {
                    // falling snow outside
                    for (int i = 0; i < 26; i++)
                    {
                        float sx = glass.Left + ((i * 0.37f + MathF.Sin(time * 0.7f + i) * 0.03f) % glass.Width + glass.Width) % glass.Width;
                        float sy = glass.Top + ((time * (0.12f + 0.04f * (i % 3)) + i * 0.29f) % glass.Height);
                        _fill.Color = SKColors.White.WithAlpha(210);
                        c.DrawCircle(sx, sy, 0.012f + 0.006f * (i % 3), _fill);
                    }
                }
                c.Restore();
                // repaint the muntins over the animation
                Rect(c, frame, glass.MidX - 0.03f, glass.Top, glass.MidX + 0.03f, glass.Bottom);
                Rect(c, frame, glass.Left, glass.MidY - 0.03f, glass.Right, glass.MidY + 0.03f);
            });
        }
    }

    private void Line(SKCanvas c, SKColor col, float w, SKRect r)
    {
        _stroke.Color = col;
        _stroke.StrokeWidth = w;
        c.DrawRect(r, _stroke);
    }

    private void Hills(SKCanvas c, SKRect g, SKColor col)
    {
        using var path = new SKPath();
        path.MoveTo(g.Left, g.Bottom);
        path.QuadTo(g.Left + g.Width * 0.3f, g.Bottom - g.Height * 0.35f, g.MidX, g.Bottom - g.Height * 0.18f);
        path.QuadTo(g.Left + g.Width * 0.8f, g.Bottom - g.Height * 0.38f, g.Right, g.Bottom - g.Height * 0.12f);
        path.LineTo(g.Right, g.Bottom);
        path.Close();
        _fill.Color = col;
        c.DrawPath(path, _fill);
    }

    private void Pine(SKCanvas c, float x, float baseY, float h)
    {
        using var path = new SKPath();
        path.MoveTo(x, baseY - h);
        path.LineTo(x + h * 0.28f, baseY);
        path.LineTo(x - h * 0.28f, baseY);
        path.Close();
        _fill.Color = new SKColor(0x0E, 0x22, 0x22);
        c.DrawPath(path, _fill);
    }

    private void Poster(SKRect r, int variant)
    {
        _wallArt.Add(c =>
        {
            RoundRect(c, new SKColor(0x0A, 0x08, 0x12), r.Left - 0.03f, r.Top - 0.03f, r.Right + 0.03f, r.Bottom + 0.03f, 0.02f);
            using (var g = SKShader.CreateLinearGradient(new SKPoint(0, r.Top), new SKPoint(0, r.Bottom),
                       new[] { _palette.Sample(variant == 0 ? 0f : 1f), _palette.Sample(0.5f), new SKColor(0x10, 0x08, 0x20) }, null, SKShaderTileMode.Clamp))
            {
                _fill.Shader = g;
                _fill.Color = SKColors.White;
                c.DrawRect(r, _fill);
                _fill.Shader = null;
            }
            if (variant == 0)
            {
                // a retro sun over a grid
                _fill.Color = new SKColor(0xFF, 0xE0, 0x80);
                c.DrawCircle(r.MidX, r.MidY, r.Width * 0.25f, _fill);
                for (int i = 0; i < 4; i++) Rect(c, new SKColor(0x10, 0x08, 0x20), r.Left, r.MidY + 0.05f + i * 0.06f, r.Right, r.MidY + 0.07f + i * 0.06f);
            }
            else
            {
                // a game controller silhouette
                RoundRect(c, new SKColor(0x10, 0x08, 0x20), r.Left + r.Width * 0.18f, r.MidY - 0.12f, r.Right - r.Width * 0.18f, r.MidY + 0.14f, 0.12f);
                _fill.Color = _palette.Sample(0.2f);
                c.DrawCircle(r.Right - r.Width * 0.32f, r.MidY, 0.04f, _fill);
                Rect(c, _palette.Sample(0.8f), r.Left + r.Width * 0.27f, r.MidY - 0.015f, r.Left + r.Width * 0.4f, r.MidY + 0.015f);
                Rect(c, _palette.Sample(0.8f), r.Left + r.Width * 0.32f, r.MidY - 0.06f, r.Left + r.Width * 0.35f, r.MidY + 0.06f);
            }
        });
    }

    private void LedStrip(float y)
    {
        _animatedBack.Add((c, glow, time, f) =>
        {
            float pulse = 0.5f + 0.5f * MathF.Min(1f, f.Bass * 1.4f + f.Pulse * 0.5f);
            for (int i = 0; i < 40; i++)
            {
                float x = -0.2f + i * 0.21f;
                var col = _palette.Sample(((i / 40f) + time * 0.1f) % 1f);
                _fill.Color = ColorUtil.Mix(PropArt.Shade(col, 0.4f), col, pulse);
                c.DrawRect(x, y - 0.015f, 0.17f, 0.03f, _fill);
                if (glow != null && i % 2 == 0)
                {
                    _fill.Color = col.WithAlpha((byte)(160 * pulse));
                    glow.DrawCircle(x + 0.1f, y + 0.08f, 0.35f, _fill);
                }
            }
        });
    }

    private void StringLights(float x0, float x1, float topY, float sag, int count)
    {
        // a draped wire across the ceiling, bulbs twinkle with the treble in palette colours
        _wallArt.Add(c =>
        {
            using var path = new SKPath();
            int swags = 4;
            float sw = (x1 - x0) / swags;
            for (int s = 0; s < swags; s++)
            {
                path.MoveTo(x0 + s * sw, topY);
                path.QuadTo(x0 + (s + 0.5f) * sw, topY + sag * 2, x0 + (s + 1) * sw, topY);
            }
            _stroke.Color = new SKColor(0x1E, 0x3A, 0x22);
            _stroke.StrokeWidth = 0.02f;
            c.DrawPath(path, _stroke);
        });
        int swagCount = 4;
        float swW = (x1 - x0) / swagCount;
        for (int i = 0; i < count; i++)
        {
            float u = (i + 0.5f) / count * swagCount;
            int s = (int)u;
            float t = u - s;
            float x = x0 + (s + t) * swW;
            float yy = topY + 2 * (1 - t) * t * sag * 2;
            _lights.Add(new RoomLight
            {
                Pos = new Vector2(x, yy + 0.04f), Color = _palette.Colors[i % _palette.Colors.Length], Kind = LightKind.Twinkle,
                BulbRadius = 0.035f, GlowRadius = 0.22f, GlowAlpha = 190, Seed = i * 7 + 3,
            });
        }
    }

    private void Backsplash(float y0, float y1, SKColor accent)
    {
        _wallArt.Add(c =>
        {
            Rect(c, new SKColor(0xF8, 0xF6, 0xF2), -4, y0, 12, y1);
            int row = 0;
            for (float y = y0; y < y1; y += 0.15f, row++)
            {
                Rect(c, new SKColor(0xD8, 0xD4, 0xCC), -4, y, 12, y + 0.008f);
                for (float x = -4 + (row % 2) * 0.15f; x < 12; x += 0.3f) Rect(c, new SKColor(0xD8, 0xD4, 0xCC), x, y, x + 0.008f, y + 0.15f);
            }
            Rect(c, accent.WithAlpha(200), -4, y0 + 0.3f, 12, y0 + 0.45f);
        });
    }

    private void CounterBlock(SKCanvas c, float x0, float x1, float topY, SKColor wood)
    {
        RoundRect(c, new SKColor(0xE8, 0xE0, 0xD4), x0, topY - 0.02f, x1, topY + 0.08f, 0.02f);   // worktop
        Rect(c, wood, x0 + 0.02f, topY + 0.08f, x1 - 0.02f, FloorY - 0.08f);
        Rect(c, PropArt.Shade(wood, 0.6f), x0 + 0.06f, FloorY - 0.1f, x1 - 0.06f, FloorY);
        for (float x = x0 + 0.05f; x < x1 - 0.3f; x += 0.6f)
        {
            RoundRect(c, PropArt.Shade(wood, 1.08f), x, topY + 0.15f, MathF.Min(x + 0.55f, x1 - 0.05f), FloorY - 0.14f, 0.02f);
            RoundRect(c, new SKColor(0x5A, 0x5A, 0x62), x + 0.2f, topY + 0.25f, MathF.Min(x + 0.35f, x1 - 0.1f), topY + 0.28f, 0.01f);
        }
    }
}
