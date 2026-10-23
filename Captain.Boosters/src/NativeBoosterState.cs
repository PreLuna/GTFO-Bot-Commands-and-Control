using System;
using System.Reflection;
using BoosterImplants;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PreLuna.Captain.Boosters
{
    internal static class NativeBoosterState
    {
        const string NativeField = "NativeMethodInfoPtr_SetBoosterImplantDatas_Public_Void_Il2CppReferenceArray_1_pBoosterImplantData_0";
        static FieldInfo methodField;
        // Invoke precisely the existing native method with a reference array.
        // The generated wrapper JITs an invalid StructArray<pBoosterImplantData>
        // even for a non-null argument, so it must never be called here.
        internal static unsafe void SetDatas(PlayerBoosterImplantState state, Il2CppReferenceArray<pBoosterImplantData> values)
        {
            if (state == null || values == null || values.Length != 3) throw new ArgumentException("强化剂清理必须提供原状态及三个引用槽位");
            methodField ??= typeof(PlayerBoosterImplantState).GetField(NativeField, BindingFlags.Static | BindingFlags.NonPublic);
            if (methodField?.FieldType != typeof(IntPtr)) throw new MissingFieldException(typeof(PlayerBoosterImplantState).FullName, NativeField);
            var method = (IntPtr)methodField.GetValue(null);
            if (method == IntPtr.Zero) throw new MissingMethodException("强化剂原生清理方法地址为空");
            var self = IL2CPP.Il2CppObjectBaseToPtrNotNull(state);
            var array = IL2CPP.Il2CppObjectBaseToPtr(values);
            if (array == IntPtr.Zero) throw new ArgumentException("强化剂清理引用数组地址为空");
            IntPtr exception = IntPtr.Zero;
            void** arguments = stackalloc void*[1]; arguments[0] = array.ToPointer();
            IL2CPP.il2cpp_runtime_invoke(method, self, arguments, ref exception);
            Il2CppException.RaiseExceptionIfNecessary(exception);
        }
    }
}
