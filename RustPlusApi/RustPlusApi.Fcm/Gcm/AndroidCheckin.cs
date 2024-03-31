// <auto-generated>
//     Generated by the protocol buffer compiler.  DO NOT EDIT!
//     source: android_checkin.proto
// </auto-generated>
#pragma warning disable 1591, 0612, 3021, 8981
#region Designer generated code

using pb = global::Google.Protobuf;
using pbc = global::Google.Protobuf.Collections;
using pbr = global::Google.Protobuf.Reflection;
using scg = global::System.Collections.Generic;
namespace CheckinProto {

  /// <summary>Holder for reflection information generated from android_checkin.proto</summary>
  public static partial class AndroidCheckinReflection {

    #region Descriptor
    /// <summary>File descriptor for android_checkin.proto</summary>
    public static pbr::FileDescriptor Descriptor {
      get { return descriptor; }
    }
    private static pbr::FileDescriptor descriptor;

    static AndroidCheckinReflection() {
      byte[] descriptorData = global::System.Convert.FromBase64String(
          string.Concat(
            "ChVhbmRyb2lkX2NoZWNraW4ucHJvdG8SDWNoZWNraW5fcHJvdG8iigMKEENo",
            "cm9tZUJ1aWxkUHJvdG8SOgoIcGxhdGZvcm0YASABKA4yKC5jaGVja2luX3By",
            "b3RvLkNocm9tZUJ1aWxkUHJvdG8uUGxhdGZvcm0SFgoOY2hyb21lX3ZlcnNp",
            "b24YAiABKAkSOAoHY2hhbm5lbBgDIAEoDjInLmNoZWNraW5fcHJvdG8uQ2hy",
            "b21lQnVpbGRQcm90by5DaGFubmVsIn0KCFBsYXRmb3JtEhAKDFBMQVRGT1JN",
            "X1dJThABEhAKDFBMQVRGT1JNX01BQxACEhIKDlBMQVRGT1JNX0xJTlVYEAMS",
            "EQoNUExBVEZPUk1fQ1JPUxAEEhAKDFBMQVRGT1JNX0lPUxAFEhQKEFBMQVRG",
            "T1JNX0FORFJPSUQQBiJpCgdDaGFubmVsEhIKDkNIQU5ORUxfU1RBQkxFEAES",
            "EAoMQ0hBTk5FTF9CRVRBEAISDwoLQ0hBTk5FTF9ERVYQAxISCg5DSEFOTkVM",
            "X0NBTkFSWRAEEhMKD0NIQU5ORUxfVU5LTk9XThAFIvYBChNBbmRyb2lkQ2hl",
            "Y2tpblByb3RvEhkKEWxhc3RfY2hlY2tpbl9tc2VjGAIgASgDEhUKDWNlbGxf",
            "b3BlcmF0b3IYBiABKAkSFAoMc2ltX29wZXJhdG9yGAcgASgJEg8KB3JvYW1p",
            "bmcYCCABKAkSEwoLdXNlcl9udW1iZXIYCSABKAUSOgoEdHlwZRgMIAEoDjIZ",
            "LmNoZWNraW5fcHJvdG8uRGV2aWNlVHlwZToRREVWSUNFX0FORFJPSURfT1MS",
            "NQoMY2hyb21lX2J1aWxkGA0gASgLMh8uY2hlY2tpbl9wcm90by5DaHJvbWVC",
            "dWlsZFByb3RvKmcKCkRldmljZVR5cGUSFQoRREVWSUNFX0FORFJPSURfT1MQ",
            "ARIRCg1ERVZJQ0VfSU9TX09TEAISGQoVREVWSUNFX0NIUk9NRV9CUk9XU0VS",
            "EAMSFAoQREVWSUNFX0NIUk9NRV9PUxAEQgJIAw=="));
      descriptor = pbr::FileDescriptor.FromGeneratedCode(descriptorData,
          new pbr::FileDescriptor[] { },
          new pbr::GeneratedClrTypeInfo(new[] {typeof(global::CheckinProto.DeviceType), }, null, new pbr::GeneratedClrTypeInfo[] {
            new pbr::GeneratedClrTypeInfo(typeof(global::CheckinProto.ChromeBuildProto), global::CheckinProto.ChromeBuildProto.Parser, new[]{ "Platform", "ChromeVersion", "Channel" }, null, new[]{ typeof(global::CheckinProto.ChromeBuildProto.Types.Platform), typeof(global::CheckinProto.ChromeBuildProto.Types.Channel) }, null, null),
            new pbr::GeneratedClrTypeInfo(typeof(global::CheckinProto.AndroidCheckinProto), global::CheckinProto.AndroidCheckinProto.Parser, new[]{ "LastCheckinMsec", "CellOperator", "SimOperator", "Roaming", "UserNumber", "Type", "ChromeBuild" }, null, null, null, null)
          }));
    }
    #endregion

  }
  #region Enums
  /// <summary>
  /// enum values correspond to the type of device.
  /// Used in the AndroidCheckinProto and Device proto.
  /// </summary>
  public enum DeviceType {
    /// <summary>
    /// Android Device
    /// </summary>
    [pbr::OriginalName("DEVICE_ANDROID_OS")] DeviceAndroidOs = 1,
    /// <summary>
    /// Apple IOS device
    /// </summary>
    [pbr::OriginalName("DEVICE_IOS_OS")] DeviceIosOs = 2,
    /// <summary>
    /// Chrome browser - Not Chrome OS.  No hardware records.
    /// </summary>
    [pbr::OriginalName("DEVICE_CHROME_BROWSER")] DeviceChromeBrowser = 3,
    /// <summary>
    /// Chrome OS
    /// </summary>
    [pbr::OriginalName("DEVICE_CHROME_OS")] DeviceChromeOs = 4,
  }

  #endregion

  #region Messages
  /// <summary>
  /// Build characteristics unique to the Chrome browser, and Chrome OS
  /// </summary>
  public sealed partial class ChromeBuildProto : pb::IMessage<ChromeBuildProto>
  #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
      , pb::IBufferMessage
  #endif
  {
    private static readonly pb::MessageParser<ChromeBuildProto> _parser = new pb::MessageParser<ChromeBuildProto>(() => new ChromeBuildProto());
    private pb::UnknownFieldSet _unknownFields;
    private int _hasBits0;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public static pb::MessageParser<ChromeBuildProto> Parser { get { return _parser; } }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public static pbr::MessageDescriptor Descriptor {
      get { return global::CheckinProto.AndroidCheckinReflection.Descriptor.MessageTypes[0]; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    pbr::MessageDescriptor pb::IMessage.Descriptor {
      get { return Descriptor; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public ChromeBuildProto() {
      OnConstruction();
    }

    partial void OnConstruction();

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public ChromeBuildProto(ChromeBuildProto other) : this() {
      _hasBits0 = other._hasBits0;
      platform_ = other.platform_;
      chromeVersion_ = other.chromeVersion_;
      channel_ = other.channel_;
      _unknownFields = pb::UnknownFieldSet.Clone(other._unknownFields);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public ChromeBuildProto Clone() {
      return new ChromeBuildProto(this);
    }

    /// <summary>Field number for the "platform" field.</summary>
    public const int PlatformFieldNumber = 1;
    private readonly static global::CheckinProto.ChromeBuildProto.Types.Platform PlatformDefaultValue = global::CheckinProto.ChromeBuildProto.Types.Platform.Win;

    private global::CheckinProto.ChromeBuildProto.Types.Platform platform_;
    /// <summary>
    /// The platform of the device.
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public global::CheckinProto.ChromeBuildProto.Types.Platform Platform {
      get { if ((_hasBits0 & 1) != 0) { return platform_; } else { return PlatformDefaultValue; } }
      set {
        _hasBits0 |= 1;
        platform_ = value;
      }
    }
    /// <summary>Gets whether the "platform" field is set</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool HasPlatform {
      get { return (_hasBits0 & 1) != 0; }
    }
    /// <summary>Clears the value of the "platform" field</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void ClearPlatform() {
      _hasBits0 &= ~1;
    }

    /// <summary>Field number for the "chrome_version" field.</summary>
    public const int ChromeVersionFieldNumber = 2;
    private readonly static string ChromeVersionDefaultValue = "";

    private string chromeVersion_;
    /// <summary>
    /// The Chrome instance's version.
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public string ChromeVersion {
      get { return chromeVersion_ ?? ChromeVersionDefaultValue; }
      set {
        chromeVersion_ = pb::ProtoPreconditions.CheckNotNull(value, "value");
      }
    }
    /// <summary>Gets whether the "chrome_version" field is set</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool HasChromeVersion {
      get { return chromeVersion_ != null; }
    }
    /// <summary>Clears the value of the "chrome_version" field</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void ClearChromeVersion() {
      chromeVersion_ = null;
    }

    /// <summary>Field number for the "channel" field.</summary>
    public const int ChannelFieldNumber = 3;
    private readonly static global::CheckinProto.ChromeBuildProto.Types.Channel ChannelDefaultValue = global::CheckinProto.ChromeBuildProto.Types.Channel.Stable;

    private global::CheckinProto.ChromeBuildProto.Types.Channel channel_;
    /// <summary>
    /// The Channel (build type) of Chrome.
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public global::CheckinProto.ChromeBuildProto.Types.Channel Channel {
      get { if ((_hasBits0 & 2) != 0) { return channel_; } else { return ChannelDefaultValue; } }
      set {
        _hasBits0 |= 2;
        channel_ = value;
      }
    }
    /// <summary>Gets whether the "channel" field is set</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool HasChannel {
      get { return (_hasBits0 & 2) != 0; }
    }
    /// <summary>Clears the value of the "channel" field</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void ClearChannel() {
      _hasBits0 &= ~2;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public override bool Equals(object other) {
      return Equals(other as ChromeBuildProto);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool Equals(ChromeBuildProto other) {
      if (ReferenceEquals(other, null)) {
        return false;
      }
      if (ReferenceEquals(other, this)) {
        return true;
      }
      if (Platform != other.Platform) return false;
      if (ChromeVersion != other.ChromeVersion) return false;
      if (Channel != other.Channel) return false;
      return Equals(_unknownFields, other._unknownFields);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public override int GetHashCode() {
      int hash = 1;
      if (HasPlatform) hash ^= Platform.GetHashCode();
      if (HasChromeVersion) hash ^= ChromeVersion.GetHashCode();
      if (HasChannel) hash ^= Channel.GetHashCode();
      if (_unknownFields != null) {
        hash ^= _unknownFields.GetHashCode();
      }
      return hash;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public override string ToString() {
      return pb::JsonFormatter.ToDiagnosticString(this);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void WriteTo(pb::CodedOutputStream output) {
    #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
      output.WriteRawMessage(this);
    #else
      if (HasPlatform) {
        output.WriteRawTag(8);
        output.WriteEnum((int) Platform);
      }
      if (HasChromeVersion) {
        output.WriteRawTag(18);
        output.WriteString(ChromeVersion);
      }
      if (HasChannel) {
        output.WriteRawTag(24);
        output.WriteEnum((int) Channel);
      }
      if (_unknownFields != null) {
        _unknownFields.WriteTo(output);
      }
    #endif
    }

    #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    void pb::IBufferMessage.InternalWriteTo(ref pb::WriteContext output) {
      if (HasPlatform) {
        output.WriteRawTag(8);
        output.WriteEnum((int) Platform);
      }
      if (HasChromeVersion) {
        output.WriteRawTag(18);
        output.WriteString(ChromeVersion);
      }
      if (HasChannel) {
        output.WriteRawTag(24);
        output.WriteEnum((int) Channel);
      }
      if (_unknownFields != null) {
        _unknownFields.WriteTo(ref output);
      }
    }
    #endif

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public int CalculateSize() {
      int size = 0;
      if (HasPlatform) {
        size += 1 + pb::CodedOutputStream.ComputeEnumSize((int) Platform);
      }
      if (HasChromeVersion) {
        size += 1 + pb::CodedOutputStream.ComputeStringSize(ChromeVersion);
      }
      if (HasChannel) {
        size += 1 + pb::CodedOutputStream.ComputeEnumSize((int) Channel);
      }
      if (_unknownFields != null) {
        size += _unknownFields.CalculateSize();
      }
      return size;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void MergeFrom(ChromeBuildProto other) {
      if (other == null) {
        return;
      }
      if (other.HasPlatform) {
        Platform = other.Platform;
      }
      if (other.HasChromeVersion) {
        ChromeVersion = other.ChromeVersion;
      }
      if (other.HasChannel) {
        Channel = other.Channel;
      }
      _unknownFields = pb::UnknownFieldSet.MergeFrom(_unknownFields, other._unknownFields);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void MergeFrom(pb::CodedInputStream input) {
    #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
      input.ReadRawMessage(this);
    #else
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            _unknownFields = pb::UnknownFieldSet.MergeFieldFrom(_unknownFields, input);
            break;
          case 8: {
            Platform = (global::CheckinProto.ChromeBuildProto.Types.Platform) input.ReadEnum();
            break;
          }
          case 18: {
            ChromeVersion = input.ReadString();
            break;
          }
          case 24: {
            Channel = (global::CheckinProto.ChromeBuildProto.Types.Channel) input.ReadEnum();
            break;
          }
        }
      }
    #endif
    }

    #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    void pb::IBufferMessage.InternalMergeFrom(ref pb::ParseContext input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            _unknownFields = pb::UnknownFieldSet.MergeFieldFrom(_unknownFields, ref input);
            break;
          case 8: {
            Platform = (global::CheckinProto.ChromeBuildProto.Types.Platform) input.ReadEnum();
            break;
          }
          case 18: {
            ChromeVersion = input.ReadString();
            break;
          }
          case 24: {
            Channel = (global::CheckinProto.ChromeBuildProto.Types.Channel) input.ReadEnum();
            break;
          }
        }
      }
    }
    #endif

    #region Nested types
    /// <summary>Container for nested types declared in the ChromeBuildProto message type.</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public static partial class Types {
      public enum Platform {
        [pbr::OriginalName("PLATFORM_WIN")] Win = 1,
        [pbr::OriginalName("PLATFORM_MAC")] Mac = 2,
        [pbr::OriginalName("PLATFORM_LINUX")] Linux = 3,
        [pbr::OriginalName("PLATFORM_CROS")] Cros = 4,
        [pbr::OriginalName("PLATFORM_IOS")] Ios = 5,
        /// <summary>
        /// Just a placeholder. Likely don't need it due to the presence of the
        /// Android GCM on phone/tablet devices.
        /// </summary>
        [pbr::OriginalName("PLATFORM_ANDROID")] Android = 6,
      }

      public enum Channel {
        [pbr::OriginalName("CHANNEL_STABLE")] Stable = 1,
        [pbr::OriginalName("CHANNEL_BETA")] Beta = 2,
        [pbr::OriginalName("CHANNEL_DEV")] Dev = 3,
        [pbr::OriginalName("CHANNEL_CANARY")] Canary = 4,
        /// <summary>
        /// for tip of tree or custom builds
        /// </summary>
        [pbr::OriginalName("CHANNEL_UNKNOWN")] Unknown = 5,
      }

    }
    #endregion

  }

  /// <summary>
  /// Information sent by the device in a "checkin" request.
  /// </summary>
  public sealed partial class AndroidCheckinProto : pb::IMessage<AndroidCheckinProto>
  #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
      , pb::IBufferMessage
  #endif
  {
    private static readonly pb::MessageParser<AndroidCheckinProto> _parser = new pb::MessageParser<AndroidCheckinProto>(() => new AndroidCheckinProto());
    private pb::UnknownFieldSet _unknownFields;
    private int _hasBits0;
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public static pb::MessageParser<AndroidCheckinProto> Parser { get { return _parser; } }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public static pbr::MessageDescriptor Descriptor {
      get { return global::CheckinProto.AndroidCheckinReflection.Descriptor.MessageTypes[1]; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    pbr::MessageDescriptor pb::IMessage.Descriptor {
      get { return Descriptor; }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public AndroidCheckinProto() {
      OnConstruction();
    }

    partial void OnConstruction();

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public AndroidCheckinProto(AndroidCheckinProto other) : this() {
      _hasBits0 = other._hasBits0;
      lastCheckinMsec_ = other.lastCheckinMsec_;
      cellOperator_ = other.cellOperator_;
      simOperator_ = other.simOperator_;
      roaming_ = other.roaming_;
      userNumber_ = other.userNumber_;
      type_ = other.type_;
      chromeBuild_ = other.chromeBuild_ != null ? other.chromeBuild_.Clone() : null;
      _unknownFields = pb::UnknownFieldSet.Clone(other._unknownFields);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public AndroidCheckinProto Clone() {
      return new AndroidCheckinProto(this);
    }

    /// <summary>Field number for the "last_checkin_msec" field.</summary>
    public const int LastCheckinMsecFieldNumber = 2;
    private readonly static long LastCheckinMsecDefaultValue = 0L;

    private long lastCheckinMsec_;
    /// <summary>
    /// Miliseconds since the Unix epoch of the device's last successful checkin.
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public long LastCheckinMsec {
      get { if ((_hasBits0 & 1) != 0) { return lastCheckinMsec_; } else { return LastCheckinMsecDefaultValue; } }
      set {
        _hasBits0 |= 1;
        lastCheckinMsec_ = value;
      }
    }
    /// <summary>Gets whether the "last_checkin_msec" field is set</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool HasLastCheckinMsec {
      get { return (_hasBits0 & 1) != 0; }
    }
    /// <summary>Clears the value of the "last_checkin_msec" field</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void ClearLastCheckinMsec() {
      _hasBits0 &= ~1;
    }

    /// <summary>Field number for the "cell_operator" field.</summary>
    public const int CellOperatorFieldNumber = 6;
    private readonly static string CellOperatorDefaultValue = "";

    private string cellOperator_;
    /// <summary>
    /// The current MCC+MNC of the mobile device's current cell.
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public string CellOperator {
      get { return cellOperator_ ?? CellOperatorDefaultValue; }
      set {
        cellOperator_ = pb::ProtoPreconditions.CheckNotNull(value, "value");
      }
    }
    /// <summary>Gets whether the "cell_operator" field is set</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool HasCellOperator {
      get { return cellOperator_ != null; }
    }
    /// <summary>Clears the value of the "cell_operator" field</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void ClearCellOperator() {
      cellOperator_ = null;
    }

    /// <summary>Field number for the "sim_operator" field.</summary>
    public const int SimOperatorFieldNumber = 7;
    private readonly static string SimOperatorDefaultValue = "";

    private string simOperator_;
    /// <summary>
    /// The MCC+MNC of the SIM card (different from operator if the
    /// device is roaming, for instance).
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public string SimOperator {
      get { return simOperator_ ?? SimOperatorDefaultValue; }
      set {
        simOperator_ = pb::ProtoPreconditions.CheckNotNull(value, "value");
      }
    }
    /// <summary>Gets whether the "sim_operator" field is set</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool HasSimOperator {
      get { return simOperator_ != null; }
    }
    /// <summary>Clears the value of the "sim_operator" field</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void ClearSimOperator() {
      simOperator_ = null;
    }

    /// <summary>Field number for the "roaming" field.</summary>
    public const int RoamingFieldNumber = 8;
    private readonly static string RoamingDefaultValue = "";

    private string roaming_;
    /// <summary>
    /// The device's current roaming state (reported starting in eclair builds).
    /// Currently one of "{,not}mobile-{,not}roaming", if it is present at all.
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public string Roaming {
      get { return roaming_ ?? RoamingDefaultValue; }
      set {
        roaming_ = pb::ProtoPreconditions.CheckNotNull(value, "value");
      }
    }
    /// <summary>Gets whether the "roaming" field is set</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool HasRoaming {
      get { return roaming_ != null; }
    }
    /// <summary>Clears the value of the "roaming" field</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void ClearRoaming() {
      roaming_ = null;
    }

    /// <summary>Field number for the "user_number" field.</summary>
    public const int UserNumberFieldNumber = 9;
    private readonly static int UserNumberDefaultValue = 0;

    private int userNumber_;
    /// <summary>
    /// For devices supporting multiple user profiles (which may be
    /// supported starting in jellybean), the ordinal number of the
    /// profile that is checking in.  This is 0 for the primary profile
    /// (which can't be changed without wiping the device), and 1,2,3,...
    /// for additional profiles (which can be added and deleted freely).
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public int UserNumber {
      get { if ((_hasBits0 & 2) != 0) { return userNumber_; } else { return UserNumberDefaultValue; } }
      set {
        _hasBits0 |= 2;
        userNumber_ = value;
      }
    }
    /// <summary>Gets whether the "user_number" field is set</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool HasUserNumber {
      get { return (_hasBits0 & 2) != 0; }
    }
    /// <summary>Clears the value of the "user_number" field</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void ClearUserNumber() {
      _hasBits0 &= ~2;
    }

    /// <summary>Field number for the "type" field.</summary>
    public const int TypeFieldNumber = 12;
    private readonly static global::CheckinProto.DeviceType TypeDefaultValue = global::CheckinProto.DeviceType.DeviceAndroidOs;

    private global::CheckinProto.DeviceType type_;
    /// <summary>
    /// Class of device.  Indicates the type of build proto
    /// (IosBuildProto/ChromeBuildProto/AndroidBuildProto)
    /// That is included in this proto
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public global::CheckinProto.DeviceType Type {
      get { if ((_hasBits0 & 4) != 0) { return type_; } else { return TypeDefaultValue; } }
      set {
        _hasBits0 |= 4;
        type_ = value;
      }
    }
    /// <summary>Gets whether the "type" field is set</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool HasType {
      get { return (_hasBits0 & 4) != 0; }
    }
    /// <summary>Clears the value of the "type" field</summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void ClearType() {
      _hasBits0 &= ~4;
    }

    /// <summary>Field number for the "chrome_build" field.</summary>
    public const int ChromeBuildFieldNumber = 13;
    private global::CheckinProto.ChromeBuildProto chromeBuild_;
    /// <summary>
    /// For devices running MCS on Chrome, build-specific characteristics
    /// of the browser.  There are no hardware aspects (except for ChromeOS).
    /// This will only be populated for Chrome builds/ChromeOS devices
    /// </summary>
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public global::CheckinProto.ChromeBuildProto ChromeBuild {
      get { return chromeBuild_; }
      set {
        chromeBuild_ = value;
      }
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public override bool Equals(object other) {
      return Equals(other as AndroidCheckinProto);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public bool Equals(AndroidCheckinProto other) {
      if (ReferenceEquals(other, null)) {
        return false;
      }
      if (ReferenceEquals(other, this)) {
        return true;
      }
      if (LastCheckinMsec != other.LastCheckinMsec) return false;
      if (CellOperator != other.CellOperator) return false;
      if (SimOperator != other.SimOperator) return false;
      if (Roaming != other.Roaming) return false;
      if (UserNumber != other.UserNumber) return false;
      if (Type != other.Type) return false;
      if (!object.Equals(ChromeBuild, other.ChromeBuild)) return false;
      return Equals(_unknownFields, other._unknownFields);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public override int GetHashCode() {
      int hash = 1;
      if (HasLastCheckinMsec) hash ^= LastCheckinMsec.GetHashCode();
      if (HasCellOperator) hash ^= CellOperator.GetHashCode();
      if (HasSimOperator) hash ^= SimOperator.GetHashCode();
      if (HasRoaming) hash ^= Roaming.GetHashCode();
      if (HasUserNumber) hash ^= UserNumber.GetHashCode();
      if (HasType) hash ^= Type.GetHashCode();
      if (chromeBuild_ != null) hash ^= ChromeBuild.GetHashCode();
      if (_unknownFields != null) {
        hash ^= _unknownFields.GetHashCode();
      }
      return hash;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public override string ToString() {
      return pb::JsonFormatter.ToDiagnosticString(this);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void WriteTo(pb::CodedOutputStream output) {
    #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
      output.WriteRawMessage(this);
    #else
      if (HasLastCheckinMsec) {
        output.WriteRawTag(16);
        output.WriteInt64(LastCheckinMsec);
      }
      if (HasCellOperator) {
        output.WriteRawTag(50);
        output.WriteString(CellOperator);
      }
      if (HasSimOperator) {
        output.WriteRawTag(58);
        output.WriteString(SimOperator);
      }
      if (HasRoaming) {
        output.WriteRawTag(66);
        output.WriteString(Roaming);
      }
      if (HasUserNumber) {
        output.WriteRawTag(72);
        output.WriteInt32(UserNumber);
      }
      if (HasType) {
        output.WriteRawTag(96);
        output.WriteEnum((int) Type);
      }
      if (chromeBuild_ != null) {
        output.WriteRawTag(106);
        output.WriteMessage(ChromeBuild);
      }
      if (_unknownFields != null) {
        _unknownFields.WriteTo(output);
      }
    #endif
    }

    #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    void pb::IBufferMessage.InternalWriteTo(ref pb::WriteContext output) {
      if (HasLastCheckinMsec) {
        output.WriteRawTag(16);
        output.WriteInt64(LastCheckinMsec);
      }
      if (HasCellOperator) {
        output.WriteRawTag(50);
        output.WriteString(CellOperator);
      }
      if (HasSimOperator) {
        output.WriteRawTag(58);
        output.WriteString(SimOperator);
      }
      if (HasRoaming) {
        output.WriteRawTag(66);
        output.WriteString(Roaming);
      }
      if (HasUserNumber) {
        output.WriteRawTag(72);
        output.WriteInt32(UserNumber);
      }
      if (HasType) {
        output.WriteRawTag(96);
        output.WriteEnum((int) Type);
      }
      if (chromeBuild_ != null) {
        output.WriteRawTag(106);
        output.WriteMessage(ChromeBuild);
      }
      if (_unknownFields != null) {
        _unknownFields.WriteTo(ref output);
      }
    }
    #endif

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public int CalculateSize() {
      int size = 0;
      if (HasLastCheckinMsec) {
        size += 1 + pb::CodedOutputStream.ComputeInt64Size(LastCheckinMsec);
      }
      if (HasCellOperator) {
        size += 1 + pb::CodedOutputStream.ComputeStringSize(CellOperator);
      }
      if (HasSimOperator) {
        size += 1 + pb::CodedOutputStream.ComputeStringSize(SimOperator);
      }
      if (HasRoaming) {
        size += 1 + pb::CodedOutputStream.ComputeStringSize(Roaming);
      }
      if (HasUserNumber) {
        size += 1 + pb::CodedOutputStream.ComputeInt32Size(UserNumber);
      }
      if (HasType) {
        size += 1 + pb::CodedOutputStream.ComputeEnumSize((int) Type);
      }
      if (chromeBuild_ != null) {
        size += 1 + pb::CodedOutputStream.ComputeMessageSize(ChromeBuild);
      }
      if (_unknownFields != null) {
        size += _unknownFields.CalculateSize();
      }
      return size;
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void MergeFrom(AndroidCheckinProto other) {
      if (other == null) {
        return;
      }
      if (other.HasLastCheckinMsec) {
        LastCheckinMsec = other.LastCheckinMsec;
      }
      if (other.HasCellOperator) {
        CellOperator = other.CellOperator;
      }
      if (other.HasSimOperator) {
        SimOperator = other.SimOperator;
      }
      if (other.HasRoaming) {
        Roaming = other.Roaming;
      }
      if (other.HasUserNumber) {
        UserNumber = other.UserNumber;
      }
      if (other.HasType) {
        Type = other.Type;
      }
      if (other.chromeBuild_ != null) {
        if (chromeBuild_ == null) {
          ChromeBuild = new global::CheckinProto.ChromeBuildProto();
        }
        ChromeBuild.MergeFrom(other.ChromeBuild);
      }
      _unknownFields = pb::UnknownFieldSet.MergeFrom(_unknownFields, other._unknownFields);
    }

    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    public void MergeFrom(pb::CodedInputStream input) {
    #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
      input.ReadRawMessage(this);
    #else
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            _unknownFields = pb::UnknownFieldSet.MergeFieldFrom(_unknownFields, input);
            break;
          case 16: {
            LastCheckinMsec = input.ReadInt64();
            break;
          }
          case 50: {
            CellOperator = input.ReadString();
            break;
          }
          case 58: {
            SimOperator = input.ReadString();
            break;
          }
          case 66: {
            Roaming = input.ReadString();
            break;
          }
          case 72: {
            UserNumber = input.ReadInt32();
            break;
          }
          case 96: {
            Type = (global::CheckinProto.DeviceType) input.ReadEnum();
            break;
          }
          case 106: {
            if (chromeBuild_ == null) {
              ChromeBuild = new global::CheckinProto.ChromeBuildProto();
            }
            input.ReadMessage(ChromeBuild);
            break;
          }
        }
      }
    #endif
    }

    #if !GOOGLE_PROTOBUF_REFSTRUCT_COMPATIBILITY_MODE
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.CodeDom.Compiler.GeneratedCode("protoc", null)]
    void pb::IBufferMessage.InternalMergeFrom(ref pb::ParseContext input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            _unknownFields = pb::UnknownFieldSet.MergeFieldFrom(_unknownFields, ref input);
            break;
          case 16: {
            LastCheckinMsec = input.ReadInt64();
            break;
          }
          case 50: {
            CellOperator = input.ReadString();
            break;
          }
          case 58: {
            SimOperator = input.ReadString();
            break;
          }
          case 66: {
            Roaming = input.ReadString();
            break;
          }
          case 72: {
            UserNumber = input.ReadInt32();
            break;
          }
          case 96: {
            Type = (global::CheckinProto.DeviceType) input.ReadEnum();
            break;
          }
          case 106: {
            if (chromeBuild_ == null) {
              ChromeBuild = new global::CheckinProto.ChromeBuildProto();
            }
            input.ReadMessage(ChromeBuild);
            break;
          }
        }
      }
    }
    #endif

  }

  #endregion

}

#endregion Designer generated code
