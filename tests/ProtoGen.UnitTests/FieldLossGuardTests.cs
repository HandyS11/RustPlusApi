using Xunit;

namespace ProtoGen.UnitTests;

/// <summary>
/// The guard that turns silent field loss into a hard failure. ProtoGen recovers fields by parsing
/// decompiled C#; when it fails to recognise a dispatch shape it produces a message with no fields
/// and no error, which reads as a deliberate server-side removal. Losing a committed field is
/// therefore treated as a tool failure until a human says otherwise.
/// </summary>
public class FieldLossGuardTests
{
    /// <summary>A message whose single field is recovered normally.</summary>
    private const string IntactSendMessage = """
                                             public class AppSendMessage : IProto<AppSendMessage>
                                             {
                                                 public string message;

                                                 public static AppSendMessage Deserialize(Stream stream, AppSendMessage instance, long limit)
                                                 {
                                                     while (true)
                                                     {
                                                         int num = stream.ReadByte();
                                                         switch (num)
                                                         {
                                                         case 10:
                                                             instance.message = ProtocolParser.ReadString(stream);
                                                             continue;
                                                         }
                                                         return instance;
                                                     }
                                                 }
                                             }
                                             """;

    /// <summary>The same message with a dispatch the parser cannot read — yields zero fields.</summary>
    private const string UnparsableSendMessage = """
                                                 public class AppSendMessage : IProto<AppSendMessage>
                                                 {
                                                     public string message;

                                                     public static AppSendMessage Deserialize(Stream stream, AppSendMessage instance, long limit)
                                                     {
                                                         return instance;
                                                     }
                                                 }
                                                 """;

    /// <summary>Builds a committed-proto baseline from literal proto lines.</summary>
    /// <param name="lines">Proto source lines.</param>
    private static CommittedProto Committed(params string[] lines) => CommittedProto.ParseLines(lines);

    /// <summary>Parses decompiled message declarations into a server model.</summary>
    /// <param name="body">Decompiled message declarations.</param>
    private static ServerParser Server(string body) =>
        ServerParser.ParseSource($$"""
                                   using System.IO;

                                   namespace ProtoBuf
                                   {
                                   {{body}}
                                   }
                                   """);

    [Fact]
    public void Check_ReportsNothing_WhenEveryCommittedFieldSurvives()
    {
        var losses = FieldLossGuard.Check(
            Committed("message AppSendMessage {", "\trequired string message = 1;", "}"),
            Server(IntactSendMessage),
            new HashSet<string>(StringComparer.Ordinal)
            {
                "AppSendMessage"
            });

        Assert.Empty(losses);
    }

    [Fact]
    public void Check_ReportsLoss_WhenMessageIsEmptiedOut()
    {
        // This is issue #120's exact signature: a committed single-field message emitted empty.
        var losses = FieldLossGuard.Check(
            Committed("message AppSendMessage {", "\trequired string message = 1;", "}"),
            Server(UnparsableSendMessage),
            new HashSet<string>(StringComparer.Ordinal)
            {
                "AppSendMessage"
            });

        var loss = Assert.Single(losses);
        Assert.Equal("AppSendMessage", loss.Message);
        Assert.Equal(1, loss.Number);
    }

    [Fact]
    public void Check_ReportsLoss_WhenOnlySomeFieldsAreRecovered()
    {
        // A partial loss is the more dangerous case: the message still looks populated, so a
        // reviewer skimming the diff sees a plausible single-field removal.
        var losses = FieldLossGuard.Check(
            Committed(
                "message AppTeamInfo {",
                "\trequired string name = 1;",
                "\trequired uint64 steam_id = 2;",
                "}"),
            Server("""
                   public class AppTeamInfo : IProto<AppTeamInfo>
                   {
                       public string name;
                       public ulong steamId;

                       public static AppTeamInfo Deserialize(Stream stream, AppTeamInfo instance, long limit)
                       {
                           while (true)
                           {
                               int num = stream.ReadByte();
                               switch (num)
                               {
                               case 10:
                                   instance.name = ProtocolParser.ReadString(stream);
                                   continue;
                               }
                               return instance;
                           }
                       }
                   }
                   """),
            new HashSet<string>(StringComparer.Ordinal)
            {
                "AppTeamInfo"
            });

        var loss = Assert.Single(losses);
        Assert.Equal("AppTeamInfo", loss.Message);
        Assert.Equal(2, loss.Number);
    }

    [Fact]
    public void Check_IgnoresMessagesOutsideScope_TheyAreEmittedVerbatim()
    {
        // Out-of-scope committed types (e.g. ClanInvitations, Vector3) are copied from the committed
        // proto rather than regenerated, so they cannot lose a field no matter what the parser did.
        var losses = FieldLossGuard.Check(
            Committed("message ClanInvitations {", "\trepeated ClanInvitations.Invitation invitations = 1;", "}"),
            Server(UnparsableSendMessage),
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Empty(losses);
    }

    [Fact]
    public void Check_ReportsNothing_WhenServerAddsNewFields()
    {
        // Additions are the normal, expected outcome of a game update — never a guard failure.
        var losses = FieldLossGuard.Check(
            Committed("message AppSendMessage {", "\trequired string message = 1;", "}"),
            Server("""
                   public class AppSendMessage : IProto<AppSendMessage>
                   {
                       public string message;
                       public bool urgent;

                       public static AppSendMessage Deserialize(Stream stream, AppSendMessage instance, long limit)
                       {
                           while (true)
                           {
                               int num = stream.ReadByte();
                               switch (num)
                               {
                               case 10:
                                   instance.message = ProtocolParser.ReadString(stream);
                                   continue;
                               case 16:
                                   instance.urgent = ProtocolParser.ReadBool(stream);
                                   continue;
                               }
                               return instance;
                           }
                       }
                   }
                   """),
            new HashSet<string>(StringComparer.Ordinal)
            {
                "AppSendMessage"
            });

        Assert.Empty(losses);
    }

    [Fact]
    public void Check_ReportsEveryLoss_NotJustTheFirst()
    {
        // Issue #120 lost 13 fields at once; a guard that reports one of them wastes a CI round trip.
        var losses = FieldLossGuard.Check(
            Committed(
                "message AppSendMessage {",
                "\trequired string message = 1;",
                "}",
                "message AppFlag {",
                "\trequired bool value = 1;",
                "}"),
            Server($"{UnparsableSendMessage}\n\npublic class AppFlag : IProto<AppFlag> {{ public bool value; }}"),
            new HashSet<string>(StringComparer.Ordinal)
            {
                "AppSendMessage", "AppFlag"
            });

        Assert.Equal(2, losses.Count);
        Assert.Contains(losses, l => l.Message == "AppSendMessage");
        Assert.Contains(losses, l => l.Message == "AppFlag");
    }
}
