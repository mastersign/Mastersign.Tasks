using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mastersign.Tasks.Test
{
    static class HardWork
    {
        private static bool IsPrime(int number)
        {
            if (number == 1) return false;
            if (number == 2) return true;

            var boundary = (int)Math.Floor(Math.Sqrt(number));

            for (int i = 2; i <= boundary; ++i)
            {
                if (number % i == 0) return false;
            }

            return true;
        }

        public static int Do(int amount = 50)
        {
            var rand = new Random();
            var x = 0;
            for (int i = 0; i < amount; i++)
            {
                var v = rand.Next();
                if (IsPrime(v)) x++;
            }
            return x;
        }

        public static int DoConstantAmount()
            => Do(50);

        public static int DoRandomAmount()
            => Do((new Random()).Next(1, 100));
    }
}
