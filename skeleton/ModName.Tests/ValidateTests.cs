using System.IO;

namespace ModName.Tests
{
    public class ValidateTests
    {
        [Theory]
        [InlineData(0f, true)]
        [InlineData(1f, true)]
        [InlineData(0.5f, true)]
        [InlineData(-0.001f, false)]
        [InlineData(1.001f, false)]
        [InlineData(float.NaN, false)]
        [InlineData(float.PositiveInfinity, false)]
        [InlineData(float.NegativeInfinity, false)]
        public void InRange_Value_ReturnsWhetherFiniteAndWithinBounds(float value, bool expected)
        {
            Assert.Equal(expected, Validate.InRange(value, 0f, 1f));
        }

        [Theory]
        [InlineData("om617", true)]
        [InlineData("Big Rig_2-b", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("../etc", false)]
        [InlineData("semi;colon", false)]
        public void IsSafeName_Input_ReturnsExpected(string name, bool expected)
        {
            Assert.Equal(expected, Validate.IsSafeName(name));
        }

        [Fact]
        public void IsSafeName_LongerThanMax_ReturnsFalse()
        {
            Assert.False(Validate.IsSafeName(new string('a', 33)));
        }

        [Fact]
        public void ShortString_RoundTrip_ReturnsSameString()
        {
            // Arrange
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                Validate.WriteShortString(w, "Wombat 300GD");
            ms.Position = 0;

            // Act
            var s = Validate.ReadShortString(new BinaryReader(ms));

            // Assert
            Assert.Equal("Wombat 300GD", s);
        }

        [Fact]
        public void WriteShortString_Over32Bytes_TruncatesTo32()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                Validate.WriteShortString(w, new string('x', 40));
            ms.Position = 0;

            Assert.Equal(new string('x', 32), Validate.ReadShortString(new BinaryReader(ms)));
        }

        [Fact]
        public void WriteShortString_Null_WritesEmptyString()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                Validate.WriteShortString(w, null);
            ms.Position = 0;

            Assert.Equal("", Validate.ReadShortString(new BinaryReader(ms)));
        }

        [Fact]
        public void ReadShortString_LengthOverMax_ReturnsNull()
        {
            var r = new BinaryReader(new MemoryStream(new byte[] { 33 }));

            Assert.Null(Validate.ReadShortString(r));
        }

        [Fact]
        public void ReadShortString_Truncated_ReturnsNull()
        {
            var r = new BinaryReader(new MemoryStream(new byte[] { 5, (byte)'a', (byte)'b' }));

            Assert.Null(Validate.ReadShortString(r));
        }

        [Fact]
        public void ReadShortString_DisallowedCharacters_ReturnsNull()
        {
            var r = new BinaryReader(new MemoryStream(new byte[] { 3, (byte)'a', (byte)'/', (byte)'b' }));

            Assert.Null(Validate.ReadShortString(r));
        }
    }
}
