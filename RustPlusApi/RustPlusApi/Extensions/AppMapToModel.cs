using System.Drawing;

using RustPlusApi.Data;

using RustPlusContracts;

using static RustPlusContracts.AppMap.Types;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions
{
    public static class AppMapToModel
    {
        public static ServerMap ToServerMap(this AppMap appMap)
        {
            return new ServerMap
            {
                Height = appMap.Height,
                Width = appMap.Width,
                OceanMargin = appMap.OceanMargin,
                Background = ColorTranslator.FromHtml(appMap.Background),
                Monuments = appMap.Monuments.ToServerMapMonuments().ToList(),
                JpgImage = appMap.JpgImage.ToByteArray()
            };
        }

        public static ServerMapMonument ToServerMapMonument(this Monument appMapMonument)
        {
            return new ServerMapMonument
            {
                Name = appMapMonument.Name,
                X = appMapMonument.X,
                Y = appMapMonument.Y
            };
        }

        public static IEnumerable<ServerMapMonument> ToServerMapMonuments(this IEnumerable<Monument> appMapMonuments)
        {
            return appMapMonuments.Select(ToServerMapMonument);
        }
    }
}
