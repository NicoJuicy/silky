﻿using System.Collections.Generic;

namespace Lms.Rpc.Runtime.Server.Parameter
{
    public static class ParameterFromExtensions
    {
        public static object DefaultValue(this ParameterFrom @form)
        {
            var defaultValue =  new Dictionary<string, object>();
            return defaultValue;
        }
    }
}