using System;
using System.Collections.Generic;

namespace VAGSuite
{
    public class CitroenMapDefinitions
    {
        public static Dictionary<string, List<MapInfo>> GetCitroenMaps()
        {
            var maps = new Dictionary<string, List<MapInfo>>();

            // EDC15C2 Maps pour Citroën
            maps.Add("CITROEN_EDC15C2", new List<MapInfo>
            {
                new MapInfo { Name = "Driver wish", Address = 0x1A240, SizeX = 16, SizeY = 16, OffsetX = 0x1A200, OffsetY = 0x1A220 },
                new MapInfo { Name = "Torque limiter", Address = 0x1A440, SizeX = 16, SizeY = 16, OffsetX = 0x1A400, OffsetY = 0x1A420 },
                new MapInfo { Name = "Smoke limiter", Address = 0x1A640, SizeX = 16, SizeY = 16, OffsetX = 0x1A600, OffsetY = 0x1A620 },
                new MapInfo { Name = "Target boost", Address = 0x1A840, SizeX = 16, SizeY = 16, OffsetX = 0x1A800, OffsetY = 0x1A820 },
                new MapInfo { Name = "Injection quantity", Address = 0x1AA40, SizeX = 16, SizeY = 16, OffsetX = 0x1AA00, OffsetY = 0x1AA20 },
                new MapInfo { Name = "Start of injection", Address = 0x1AC40, SizeX = 16, SizeY = 16, OffsetX = 0x1AC00, OffsetY = 0x1AC20 },
                new MapInfo { Name = "Rail pressure", Address = 0x1AE40, SizeX = 16, SizeY = 16, OffsetX = 0x1AE00, OffsetY = 0x1AE20 }
            });

            // EDC16C34 Maps pour Citroën
            maps.Add("CITROEN_EDC16C34", new List<MapInfo>
            {
                new MapInfo { Name = "Driver wish", Address = 0x2A240, SizeX = 18, SizeY = 18, OffsetX = 0x2A200, OffsetY = 0x2A220 },
                new MapInfo { Name = "Torque limiter", Address = 0x2A4C0, SizeX = 18, SizeY = 18, OffsetX = 0x2A480, OffsetY = 0x2A4A0 },
                new MapInfo { Name = "Smoke limiter", Address = 0x2A740, SizeX = 18, SizeY = 18, OffsetX = 0x2A700, OffsetY = 0x2A720 },
                new MapInfo { Name = "Target boost", Address = 0x2A9C0, SizeX = 18, SizeY = 18, OffsetX = 0x2A980, OffsetY = 0x2A9A0 },
                new MapInfo { Name = "Injection quantity", Address = 0x2AC40, SizeX = 18, SizeY = 18, OffsetX = 0x2AC00, OffsetY = 0x2AC20 },
                new MapInfo { Name = "Start of injection", Address = 0x2AEC0, SizeX = 18, SizeY = 18, OffsetX = 0x2AE80, OffsetY = 0x2AEA0 },
                new MapInfo { Name = "Rail pressure", Address = 0x2B140, SizeX = 18, SizeY = 18, OffsetX = 0x2B100, OffsetY = 0x2B120 },
                new MapInfo { Name = "EGR position", Address = 0x2B3C0, SizeX = 18, SizeY = 18, OffsetX = 0x2B380, OffsetY = 0x2B3A0 },
                new MapInfo { Name = "Turbo position", Address = 0x2B640, SizeX = 18, SizeY = 18, OffsetX = 0x2B600, OffsetY = 0x2B620 }
            });

            // EDC17C60 Maps pour Citroën
            maps.Add("CITROEN_EDC17C60", new List<MapInfo>
            {
                new MapInfo { Name = "Driver wish", Address = 0x4A240, SizeX = 20, SizeY = 20, OffsetX = 0x4A200, OffsetY = 0x4A220 },
                new MapInfo { Name = "Torque limiter", Address = 0x4A540, SizeX = 20, SizeY = 20, OffsetX = 0x4A500, OffsetY = 0x4A520 },
                new MapInfo { Name = "Smoke limiter", Address = 0x4A840, SizeX = 20, SizeY = 20, OffsetX = 0x4A800, OffsetY = 0x4A820 },
                new MapInfo { Name = "Target boost", Address = 0x4AB40, SizeX = 20, SizeY = 20, OffsetX = 0x4AB00, OffsetY = 0x4AB20 },
                new MapInfo { Name = "Injection quantity", Address = 0x4AE40, SizeX = 20, SizeY = 20, OffsetX = 0x4AE00, OffsetY = 0x4AE20 },
                new MapInfo { Name = "Start of injection", Address = 0x4B140, SizeX = 20, SizeY = 20, OffsetX = 0x4B100, OffsetY = 0x4B120 },
                new MapInfo { Name = "Rail pressure", Address = 0x4B440, SizeX = 20, SizeY = 20, OffsetX = 0x4B400, OffsetY = 0x4B420 },
                new MapInfo { Name = "EGR position", Address = 0x4B740, SizeX = 20, SizeY = 20, OffsetX = 0x4B700, OffsetY = 0x4B720 },
                new MapInfo { Name = "Turbo position", Address = 0x4BA40, SizeX = 20, SizeY = 20, OffsetX = 0x4BA00, OffsetY = 0x4BA20 },
                new MapInfo { Name = "Air mass", Address = 0x4BD40, SizeX = 20, SizeY = 20, OffsetX = 0x4BD00, OffsetY = 0x4BD20 },
                new MapInfo { Name = "Fuel temperature", Address = 0x4C040, SizeX = 16, SizeY = 16, OffsetX = 0x4C000, OffsetY = 0x4C020 }
            });

            // Maps PSA génériques (partagées Peugeot/Citroën)
            maps.Add("PSA_EDC15C2", maps["CITROEN_EDC15C2"]);
            maps.Add("PSA_EDC16C34", maps["CITROEN_EDC16C34"]);
            maps.Add("PSA_EDC17C60", maps["CITROEN_EDC17C60"]);

            return maps;
        }
    }

    // Structure pour définir une carte
    public class MapInfo
    {
        public string Name { get; set; }
        public long Address { get; set; }
        public int SizeX { get; set; }
        public int SizeY { get; set; }
        public long OffsetX { get; set; }
        public long OffsetY { get; set; }
        public List<string> AlternativeNames { get; set; } = new List<string>();
    }
}