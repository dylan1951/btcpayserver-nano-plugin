using System.Globalization;
using System.Numerics;
using Nano.Net.Numbers;

namespace BTCPayServer.Plugins.Nano.Utils
{
    public class NanoMoney
    {
        private static readonly BigDecimal _factor = BigInteger.Pow(10, 30);
        private static readonly BigDecimal _inverseFactor = new(BigInteger.One, -30);
        
        public static decimal Convert(BigInteger raw)
        {
            var bigDecimal = new BigDecimal(raw, -30);
            bigDecimal.Truncate(12);
            return decimal.Parse(bigDecimal.ToString(), CultureInfo.InvariantCulture);
        }

        public static BigInteger Convert(decimal nano)
        {
            var result = BigDecimal.Parse(nano) * _factor;
            return (BigInteger)result;
        }
    }
}

