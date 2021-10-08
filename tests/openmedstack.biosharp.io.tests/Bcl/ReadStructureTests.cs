namespace OpenMedStack.BioSharp.Io.Tests.Bcl
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Model.Bcl;
    using Xunit;

    public class ReadStructureTests
    {
        [Fact]
        public void CanParse()
        {
            var structure = ReadStructure.Parse("25T8B25T");

            Assert.Equal(
                new List<Read>
                {
                    new() { IsIndexedRead = "N", Number = 1, NumCycles = 25, Type = ReadType.T },
                    new() { IsIndexedRead = "Y", Number = 2, NumCycles = 8, Type = ReadType.B },
                    new() { IsIndexedRead = "N", Number = 3, NumCycles = 25, Type = ReadType.T }
                }.AsEnumerable(),
                structure.Reads,
                new ReadComparer());
        }

        private class ReadComparer : IEqualityComparer<Read>
        {
            public bool Equals(Read x, Read y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (ReferenceEquals(x, null))
                {
                    return false;
                }

                if (ReferenceEquals(y, null))
                {
                    return false;
                }

                if (x.GetType() != y.GetType())
                {
                    return false;
                }

                return x.Type == y.Type && x.Number == y.Number && x.NumCycles == y.NumCycles;
            }

            public int GetHashCode(Read obj)
            {
                return HashCode.Combine((int)obj.Type, obj.Number, obj.NumCycles);
            }
        }
    }
}