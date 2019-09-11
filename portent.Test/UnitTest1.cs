using Xunit;

namespace portent.Test
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            Assert.Equal(4, FakeClass.TestMe());
        }
    }
}
