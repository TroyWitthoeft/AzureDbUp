using System;
using Xunit;
using AzureDbUp;

namespace tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test1()
        {
            var program = new Program();
            var maskedString = program.MaskConnection("password=SuperSecret!");
            var passMasked = !maskedString.Contains("SuperSecret!");
            Assert.True(passMasked);
        }
    }
}
