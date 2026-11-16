using System;
using System.IO;
using System.Text;

namespace WombatGD.Tests
{
    /// <summary>
    /// Profile packets arrive from other players, so Deserialize is an input-validation
    /// boundary: anything malformed or out of range must return null, never throw.
    /// </summary>
    public class ProfileWireTests
    {
        private const int FloatCount = 17;
        private const int FloatsOffsetWithEmptyStrings = 1 + 4 + 1 + 1 + 1; // version, id, flags, 2 x len

        private static Profile ValidProfile() => new()
        {
            MirrorSnorkel = true,
            PillarAntenna = true,
            DieselLayerEnabled = true,
            SootyExhaust = true,
            EngineSoundLabel = "Bulldog",
            DieselSoundSet = "om617",
            DieselVolume = 1f,
            StockEngineVolume = 0.5f,
            CoalAmount = 2f,
            IdleRate = 10f,
            ThrottleRate = 100f,
            PuffRate = 50f,
            Size = 1f,
            Linger = 1f,
            Darkness = 0.8f,
            Density = 0.5f,
            JetSpeed = 5f,
            Billow = 1f,
            Rise = 1f,
            Spread = 1f,
            ScreenRate = 100f,
            ScreenSize = 2f,
            ScreenLinger = 2f,
        };

        /// <summary>A packet with empty strings and every float set to <paramref name="fill"/>.</summary>
        private static byte[] PacketWithFloats(Func<int, float> fill)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Profile.WireVersion);
            w.Write(7u);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((byte)0);
            for (int i = 0; i < FloatCount; i++) w.Write(fill(i));
            return ms.ToArray();
        }

        private static float InRange(int i) => i switch
        {
            6 or 7 or 15 => 1f,   // ranges starting at 0.3
            9 => 0.5f,            // density starts at 0.1
            13 or 16 => 1f,       // ranges starting at 0.2
            _ => 0.5f,
        };

        [Fact]
        public void Deserialize_SerializedProfile_RoundTripsAllFields()
        {
            // Arrange
            var original = ValidProfile();

            // Act
            var copy = Profile.Deserialize(original.Serialize(42u), out uint id);

            // Assert
            Assert.NotNull(copy);
            Assert.Equal(42u, id);
            Assert.Equal(original.Serialize(42u), copy.Serialize(42u));
        }

        [Fact]
        public void Deserialize_AllFlagsSet_RestoresEachFlag()
        {
            // Arrange
            var p = ValidProfile();
            p.HideStockExhaust = true;
            p.SmokeScreenActive = true;

            // Act
            var copy = Profile.Deserialize(p.Serialize(1u), out _);

            // Assert
            Assert.True(copy.MirrorSnorkel && copy.PillarAntenna && copy.DieselLayerEnabled
                        && copy.HideStockExhaust && copy.SootyExhaust && copy.SmokeScreenActive);
        }

        [Fact]
        public void Deserialize_PacketWithInRangeFloats_ReturnsProfile()
        {
            var p = Profile.Deserialize(PacketWithFloats(InRange), out uint id);

            Assert.NotNull(p);
            Assert.Equal(7u, id);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        [InlineData(-0.01f)]
        [InlineData(10000f)]
        public void Deserialize_FloatOutsideRange_ReturnsNull(float bad)
        {
            // Arrange: every field valid except the one under test, for each field
            for (int field = 0; field < FloatCount; field++)
            {
                var data = PacketWithFloats(i => i == field ? bad : InRange(i));

                // Act
                var p = Profile.Deserialize(data, out _);

                // Assert
                Assert.True(p == null, $"field {field} accepted {bad}");
            }
        }

        [Fact]
        public void Deserialize_WrongVersion_ReturnsNull()
        {
            var data = ValidProfile().Serialize(1u);
            data[0] = Profile.WireVersion + 1;

            Assert.Null(Profile.Deserialize(data, out _));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(513)]
        public void Deserialize_BadLength_ReturnsNull(int length)
        {
            Assert.Null(Profile.Deserialize(new byte[length], out _));
        }

        [Fact]
        public void Deserialize_NullData_ReturnsNull()
        {
            Assert.Null(Profile.Deserialize(null, out _));
        }

        [Fact]
        public void Deserialize_TruncatedPacket_ReturnsNullWithoutThrowing()
        {
            var full = ValidProfile().Serialize(1u);

            for (int len = 6; len < full.Length; len++)
                Assert.Null(Profile.Deserialize(full.AsSpan(0, len).ToArray(), out _));
        }

        [Theory]
        [InlineData("bad;name")]
        [InlineData("../etc")]
        [InlineData("tab\there")]
        public void Deserialize_LabelWithDisallowedCharacters_ReturnsNull(string label)
        {
            var p = ValidProfile();
            p.EngineSoundLabel = label;

            Assert.Null(Profile.Deserialize(p.Serialize(1u), out _));
        }

        [Fact]
        public void Deserialize_StringLengthOver32_ReturnsNull()
        {
            var data = PacketWithFloats(InRange);
            data[FloatsOffsetWithEmptyStrings - 2] = 33; // label length byte

            Assert.Null(Profile.Deserialize(data, out _));
        }

        [Fact]
        public void Serialize_LabelLongerThan32Bytes_IsTruncatedTo32()
        {
            var p = ValidProfile();
            p.EngineSoundLabel = new string('a', 40);

            var copy = Profile.Deserialize(p.Serialize(1u), out _);

            Assert.Equal(new string('a', 32), copy.EngineSoundLabel);
        }

        [Fact]
        public void Serialize_NullStrings_WritesEmptyStrings()
        {
            var p = ValidProfile();
            p.EngineSoundLabel = null;
            p.DieselSoundSet = null;

            var copy = Profile.Deserialize(p.Serialize(1u), out _);

            Assert.Equal("", copy.EngineSoundLabel);
            Assert.Equal("", copy.DieselSoundSet);
        }

        [Fact]
        public void Serialize_Profile_HasDocumentedLayout()
        {
            // [version:1][vehicleId:4][flags:1][label:str][soundSet:str][17 x float32]
            var p = ValidProfile();

            var data = p.Serialize(0x01020304u);

            int strings = 2 + Encoding.UTF8.GetByteCount(p.EngineSoundLabel) + Encoding.UTF8.GetByteCount(p.DieselSoundSet);
            Assert.Equal(1 + 4 + 1 + strings + FloatCount * 4, data.Length);
            Assert.Equal(Profile.WireVersion, data[0]);
            Assert.Equal(0x01020304u, BitConverter.ToUInt32(data, 1));
        }
    }
}
