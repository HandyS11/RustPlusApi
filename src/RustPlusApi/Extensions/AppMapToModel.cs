using RustPlusApi.Data;
using RustPlusApi.Utils;
using RustPlusContracts;
using System.Drawing;
using static RustPlusContracts.AppMap;
// ReSharper disable MemberCanBePrivate.Global

namespace RustPlusApi.Extensions;

/// <summary>Mapping extensions from protobuf map messages to model types.</summary>
public static class AppMapToModel
{
    /// <summary>Maps an <see cref="AppMap"/> to a <see cref="ServerMap"/>.</summary>
    /// <param name="appMap">The protobuf map response.</param>
    public static ServerMap ToServerMap(this AppMap appMap)
    {
        return new ServerMap
        {
            Height = appMap.Height,
            Width = appMap.Width,
            OceanMargin = appMap.OceanMargin,
            Background = HtmlColorParser.FromHtml(appMap.Background),
            Monuments = [.. appMap.Monuments.ToServerMapMonuments()],
            JpgImage = appMap.JpgImage
        };
    }

    /// <summary>Maps a single protobuf monument to a <see cref="ServerMapMonument"/>.</summary>
    /// <param name="appMapMonument">The protobuf monument.</param>
    public static ServerMapMonument ToServerMapMonument(this Monument appMapMonument)
    {
        return new ServerMapMonument
        {
            // Server field is `token` (a localization key, e.g. "lighthouse"); exposed as Name.
            Name = appMapMonument.Token,
            X = appMapMonument.X,
            Y = appMapMonument.Y
        };
    }

    /// <summary>Maps a sequence of protobuf monuments to <see cref="ServerMapMonument"/> instances.</summary>
    /// <param name="appMapMonuments">The protobuf monuments to map.</param>
    public static IEnumerable<ServerMapMonument> ToServerMapMonuments(this IEnumerable<Monument> appMapMonuments)
    {
        return appMapMonuments.Select(ToServerMapMonument);
    }
}
