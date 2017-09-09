using SharedLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharedLibrary.Utils
{
    public class EnumHelper
    {
        public static string StatusToString(Enums.Status status)
        {
            switch (status)
            {
                case Enums.Status.Normal:
                    return "Normal";
                case Enums.Status.Fixed:
                    return "Fixed";
            }

            return String.Empty;
        }
    }
}
