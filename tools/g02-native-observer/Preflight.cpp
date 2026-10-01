#include <windows.h>
#include <cor.h>
#include <corprof.h>
#include <cstdio>
#include <type_traits>

#ifndef _M_X64
#error The G02 observer preflight must be compiled for x64.
#endif

// Declaration compatibility only: no CLR is loaded and no profiler is registered.
using EnterHooks = HRESULT (STDMETHODCALLTYPE ICorProfilerInfo3::*)(
    FunctionEnter3*, FunctionLeave3*, FunctionTailcall3*);
using IdMapper = HRESULT (STDMETHODCALLTYPE ICorProfilerInfo3::*)(FunctionIDMapper2*, void*);
static_assert(sizeof(void*) == 8, "The G02 observer target is Windows x64");
static_assert(sizeof(FunctionID) == 8, "FunctionID must retain the full native identifier");
static_assert(std::is_same_v<decltype(&ICorProfilerInfo3::SetEnterLeaveFunctionHooks3), EnterHooks>);
static_assert(std::is_same_v<decltype(&ICorProfilerInfo3::SetFunctionIDMapper2), IdMapper>);

int main()
{
    std::puts("{\"purpose\":\"declaration-preflight-only\",\"architecture\":\"x64\",\"pointerBytes\":8,\"functionIdBytes\":8,\"enterHooks3Declared\":true,\"idMapper2Declared\":true,\"clrLoaded\":false}");
    return 0;
}
