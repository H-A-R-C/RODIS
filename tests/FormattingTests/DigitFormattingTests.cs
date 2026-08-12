using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;

namespace STEDIUnitTests.FormattingTests
{
    [TestClass]
    public class FormatWithLeadingZerosTests
    {
        // ---------------------------------------------------------
        // INT TESTS
        // ---------------------------------------------------------

        [TestMethod]
        public void Int_PositiveValue_PadsCorrectly()
        {
            int value = 42;
            string result = FormatHelpers.FormatWithLeadingZeros(value, 5);
            Assert.AreEqual("00042", result);
        }

        [TestMethod]
        public void Int_NegativeValue_PadsCorrectly()
        {
            int value = -42;
            string result = FormatHelpers.FormatWithLeadingZeros(value, 5);
            Assert.AreEqual("-00042", result);
        }

        [TestMethod]
        public void Int_Zero_PadsCorrectly()
        {
            int value = 0;
            string result = FormatHelpers.FormatWithLeadingZeros(value, 4);
            Assert.AreEqual("0000", result);
        }


        // ---------------------------------------------------------
        // LONG TESTS
        // ---------------------------------------------------------

        [TestMethod]
        public void Long_PositiveValue_PadsCorrectly()
        {
            long value = 9876543210L;
            string result = FormatHelpers.FormatWithLeadingZeros(value, 15);
            Assert.AreEqual("000009876543210", result);
        }

        [TestMethod]
        public void Long_NegativeValue_PadsCorrectly()
        {
            long value = -12345L;
            string result = FormatHelpers.FormatWithLeadingZeros(value, 10);
            Assert.AreEqual("-0000012345", result);
        }


        // ---------------------------------------------------------
        // BIGINTEGER TESTS
        // ---------------------------------------------------------

        [TestMethod]
        public void BigInteger_PositiveValue_PadsCorrectly()
        {
            BigInteger value = BigInteger.Parse("123456789");
            string result = FormatHelpers.FormatWithLeadingZeros(value, 12);
            Assert.AreEqual("000123456789", result);
        }

        [TestMethod]
        public void BigInteger_NegativeValue_PadsCorrectly()
        {
            BigInteger value = BigInteger.Parse("-999999999999");
            string result = FormatHelpers.FormatWithLeadingZeros(value, 15);
            Assert.AreEqual("-000999999999999", result);
        }


        // ---------------------------------------------------------
        // GENERIC VERSION TESTS (INumber<T>)
        // ---------------------------------------------------------

        [TestMethod]
        public void Generic_Int_PadsCorrectly()
        {
            string result = FormatHelpers.FormatWithLeadingZerosGeneric(-123, 6);
            Assert.AreEqual("-000123", result);
        }

        [TestMethod]
        public void Generic_Long_PadsCorrectly()
        {
            string result = FormatHelpers.FormatWithLeadingZerosGeneric(123456L, 10);
            Assert.AreEqual("0000123456", result);
        }

        [TestMethod]
        public void Generic_BigInteger_PadsCorrectly()
        {
            BigInteger value = BigInteger.Parse("42");
            string result = FormatHelpers.FormatWithLeadingZerosGeneric(value, 5);
            Assert.AreEqual("00042", result);
        }

        [TestMethod]
        public void Generic_Zero_PadsCorrectly()
        {
            string result = FormatHelpers.FormatWithLeadingZerosGeneric(BigInteger.Zero, 3);
            Assert.AreEqual("000", result);
        }
    }
}

