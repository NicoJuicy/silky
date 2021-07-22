using System.Diagnostics;
using System.Threading.Tasks;
using Silky.Core.DynamicProxy;
using Silky.Rpc.Runtime.Server;

namespace Silky.Transaction.Tcc
{
    public static class SilkyMethodInvocationExtensions
    {
        public static async Task ExcuteTccMethod(this ISilkyMethodInvocation invocation, TccMethodType tccMethodType)
        {
            var serviceEntry = invocation.ArgumentsDictionary["serviceEntry"] as ServiceEntry;
            Debug.Assert(serviceEntry != null);
            var serviceKey = invocation.ArgumentsDictionary["serviceKey"] as string;
            var parameters = invocation.ArgumentsDictionary["parameters"] as object[];
            if (serviceEntry.IsLocal)
            {
                var excutorInfo = serviceEntry.GetTccExcutorInfo(serviceKey, tccMethodType);
                if (excutorInfo.Item2)
                {
                    invocation.ReturnValue = await excutorInfo.Item1.ExecuteAsync(excutorInfo.Item3, parameters);
                }
                else
                {
                    invocation.ReturnValue = excutorInfo.Item1.Execute(excutorInfo.Item3, parameters);
                }
            }
            else
            {
                await invocation.ProceedAsync();
            }
            
        }
    }
}