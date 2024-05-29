using System.Drawing;

namespace RustPlusApi.Data
{
    public class ServerMap
    {
        public uint? Height { get; set; }
        public uint? Width { get; set; }
        public int? OceanMargin { get; set; }
        public Color Background { get; set; }
        public List<ServerMapMonument>? Monuments { get; set; }
        public byte[]? JpgImage { get; set; }
    }
}
