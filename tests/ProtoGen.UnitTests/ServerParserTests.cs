using Xunit;

namespace ProtoGen.UnitTests;

/// <summary>
/// Field recovery from the decompiled <c>Deserialize</c> method. The decompiler chooses how to
/// render the field dispatch (switch vs if/else-if) and that choice is not under our control, so
/// every shape it can emit must recover the same fields.
/// </summary>
public class ServerParserTests
{
    /// <summary>Wraps message bodies in the namespace the parser scopes to.</summary>
    /// <param name="body">Decompiled message declarations.</param>
    private static string Source(string body) =>
        $$"""
          using System.IO;

          namespace ProtoBuf
          {
          {{body}}
          }
          """;

    [Fact]
    public void ParseSource_RecoversField_FromSwitchDispatch()
    {
        var parser = ServerParser.ParseSource(Source("""
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
                                                     """));

        var field = Assert.Single(parser.Messages["AppSendMessage"].Fields);
        Assert.Equal("message", field.Name);
        Assert.Equal(1, field.Number);
        Assert.Equal("string", field.ProtoType);
    }

    [Fact]
    public void ParseSource_RecoversField_FromIfDispatch()
    {
        var parser = ServerParser.ParseSource(Source("""
                                                     public class AppSendMessage : IProto<AppSendMessage>
                                                     {
                                                         public string message;

                                                         public static AppSendMessage Deserialize(Stream stream, AppSendMessage instance, long limit)
                                                         {
                                                             while (true)
                                                             {
                                                                 int num = stream.ReadByte();
                                                                 if (num == 10)
                                                                 {
                                                                     instance.message = ProtocolParser.ReadString(stream);
                                                                     continue;
                                                                 }
                                                                 return instance;
                                                             }
                                                         }
                                                     }
                                                     """));

        var field = Assert.Single(parser.Messages["AppSendMessage"].Fields);
        Assert.Equal("message", field.Name);
        Assert.Equal(1, field.Number);
        Assert.Equal("string", field.ProtoType);
    }

    [Fact]
    public void ParseSource_RecoversAllFields_FromElseIfChain()
    {
        var parser = ServerParser.ParseSource(Source("""
                                                     public class AppTeamInfo : IProto<AppTeamInfo>
                                                     {
                                                         public string name;
                                                         public ulong steamId;

                                                         public static AppTeamInfo Deserialize(Stream stream, AppTeamInfo instance, long limit)
                                                         {
                                                             while (true)
                                                             {
                                                                 int num = stream.ReadByte();
                                                                 if (num == 10)
                                                                 {
                                                                     instance.name = ProtocolParser.ReadString(stream);
                                                                     continue;
                                                                 }
                                                                 else if (num == 16)
                                                                 {
                                                                     instance.steamId = ProtocolParser.ReadUInt64(stream);
                                                                     continue;
                                                                 }
                                                                 return instance;
                                                             }
                                                         }
                                                     }
                                                     """));

        var fields = parser.Messages["AppTeamInfo"].Fields;
        Assert.Equal(2, fields.Count);
        Assert.Equal(("name", 1, "string"), (fields[0].Name, fields[0].Number, fields[0].ProtoType));
        Assert.Equal(("steamId", 2, "uint64"), (fields[1].Name, fields[1].Number, fields[1].ProtoType));
    }

    [Fact]
    public void ParseSource_TreatsKeyFieldComparison_AsFieldNumberNotWireKey()
    {
        // `key.Field == 33` is already a field number; `num == 10` is a wire key needing >> 3.
        // Getting this backwards would silently renumber a field.
        var parser = ServerParser.ParseSource(Source("""
                                                     public class AppTeamKick : IProto<AppTeamKick>
                                                     {
                                                         public ulong steamId;

                                                         public static AppTeamKick Deserialize(Stream stream, AppTeamKick instance, long limit)
                                                         {
                                                             while (true)
                                                             {
                                                                 int num = stream.ReadByte();
                                                                 Key key = ProtocolParser.ReadKey((byte)num, stream);
                                                                 if (key.Field == 33)
                                                                 {
                                                                     instance.steamId = ProtocolParser.ReadUInt64(stream);
                                                                     continue;
                                                                 }
                                                                 return instance;
                                                             }
                                                         }
                                                     }
                                                     """));

        var field = Assert.Single(parser.Messages["AppTeamKick"].Fields);
        Assert.Equal(33, field.Number);
    }

    [Fact]
    public void ParseSource_RecoversFields_FromMixedSwitchAndIfDispatch()
    {
        // The real generated shape dispatches low field numbers on the raw key byte and high ones
        // on key.Field, so both forms can appear in one Deserialize.
        var parser = ServerParser.ParseSource(Source("""
                                                     public class AppRequest : IProto<AppRequest>
                                                     {
                                                         public uint seq;
                                                         public ulong kicked;

                                                         public static AppRequest Deserialize(Stream stream, AppRequest instance, long limit)
                                                         {
                                                             while (true)
                                                             {
                                                                 int num = stream.ReadByte();
                                                                 switch (num)
                                                                 {
                                                                 case 8:
                                                                     instance.seq = ProtocolParser.ReadUInt32(stream);
                                                                     continue;
                                                                 }
                                                                 Key key = ProtocolParser.ReadKey((byte)num, stream);
                                                                 if (key.Field == 33)
                                                                 {
                                                                     instance.kicked = ProtocolParser.ReadUInt64(stream);
                                                                     continue;
                                                                 }
                                                                 return instance;
                                                             }
                                                         }
                                                     }
                                                     """));

        var fields = parser.Messages["AppRequest"].Fields;
        Assert.Equal(2, fields.Count);
        Assert.Equal((1, "seq"), (fields[0].Number, fields[0].Name));
        Assert.Equal((33, "kicked"), (fields[1].Number, fields[1].Name));
    }

    [Fact]
    public void ParseSource_IgnoresNonPositiveLabels_SuchAsEndOfStreamAndInvalidFieldZero()
    {
        // ILSpy folds the `keyByte == -1` end-of-stream guard into the dispatch, and the generator
        // emits a `case 0` that throws. Neither is a field.
        var parser = ServerParser.ParseSource(Source("""
                                                     public class AppFlag : IProto<AppFlag>
                                                     {
                                                         public bool value;

                                                         public static AppFlag Deserialize(Stream stream, AppFlag instance, long limit)
                                                         {
                                                             while (true)
                                                             {
                                                                 int num = stream.ReadByte();
                                                                 switch (num)
                                                                 {
                                                                 case -1:
                                                                     throw new EndOfStreamException();
                                                                 case 8:
                                                                     instance.value = ProtocolParser.ReadBool(stream);
                                                                     continue;
                                                                 }
                                                                 Key key = ProtocolParser.ReadKey((byte)num, stream);
                                                                 if (key.Field == 0)
                                                                 {
                                                                     throw new InvalidDataException("Invalid field id: 0");
                                                                 }
                                                                 return instance;
                                                             }
                                                         }
                                                     }
                                                     """));

        var field = Assert.Single(parser.Messages["AppFlag"].Fields);
        Assert.Equal(1, field.Number);
        Assert.Equal("bool", field.ProtoType);
    }
}
