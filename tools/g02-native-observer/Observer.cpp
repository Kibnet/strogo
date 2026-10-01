#include <windows.h>
#include <initguid.h>
#include <cor.h>
#include <corprof.h>
#include <cstdio>
#include <cwchar>
#ifndef _M_X64
#error Windows x64 required
#endif
#pragma warning(disable:4100)
// Diagnostic fixture observer. Host-controlled environment, no registry registration.
static const CLSID ObserverId = {0x486d81e7,0xf308,0x4e40,{0x91,0x42,0x58,0x29,0x0b,0x2a,0x6e,0x11}};
static ICorProfilerInfo3* info = nullptr;
static HANDLE trace = INVALID_HANDLE_VALUE;
static SRWLOCK traceLock = SRWLOCK_INIT;
static unsigned bytesWritten = 0, entries = 0;
static bool traceFailed = false;
static wchar_t targetType[256], targetMethod[256];
static void Write(const char* text) {
  AcquireSRWLockExclusive(&traceLock);
  const size_t length = strlen(text);
  DWORD written = 0;
  if (trace == INVALID_HANDLE_VALUE || length > 1024 || bytesWritten + length > 1048576 ||
      !WriteFile(trace,text,static_cast<DWORD>(length),&written,nullptr) || written != length) traceFailed = true;
  else bytesWritten += written;
  ReleaseSRWLockExclusive(&traceLock);
}
static SRWLOCK lifecycleLock = SRWLOCK_INIT;
struct MapperLease { MapperLease() { AcquireSRWLockShared(&lifecycleLock); } ~MapperLease() { ReleaseSRWLockShared(&lifecycleLock); } };
extern "C" volatile LONG ObserverActive;
static UINT_PTR __stdcall Map(FunctionID id, void*, BOOL* hook) {
  MapperLease lease;
  if (InterlockedCompareExchange(&ObserverActive,0,0) == 0) { *hook=FALSE; return 0; }
  *hook = FALSE;
  ClassID cls; ModuleID module; mdToken token;
  if (!info || FAILED(info->GetFunctionInfo(id,&cls,&module,&token))) return 0;
  IMetaDataImport* metadata = nullptr;
  if (FAILED(info->GetModuleMetaData(module,ofRead,IID_IMetaDataImport,reinterpret_cast<IUnknown**>(&metadata)))) return 0;
  mdTypeDef type; wchar_t methodName[256]={}, typeName[256]={}; ULONG chars=0;
  HRESULT hr = metadata->GetMethodProps(token,&type,methodName,256,&chars,nullptr,nullptr,nullptr,nullptr,nullptr);
  if (SUCCEEDED(hr) && (chars == 0 || chars > 256)) hr=E_INVALIDARG;
  if (SUCCEEDED(hr)) hr = metadata->GetTypeDefProps(type,typeName,256,&chars,nullptr,nullptr);
  metadata->Release();
  if (FAILED(hr) || chars == 0 || chars > 256 || wcscmp(methodName,targetMethod) || wcscmp(typeName,targetType)) return 0;
  char line[256];
  sprintf_s(line,"{\"event\":\"map\",\"pid\":%lu,\"functionId\":\"%llu\",\"moduleId\":\"%llu\",\"token\":%u}\n",GetCurrentProcessId(),static_cast<unsigned long long>(id),static_cast<unsigned long long>(module),token);
  Write(line); *hook = TRUE; return id;
}
extern "C" {
  alignas(8) volatile LONG64 ObserverCount = 0;
  volatile LONG ObserverActive = 0, ObserverHooks = 0, ObserverOverflow = 0;
  alignas(8) UINT_PTR ObserverSlots[4096] = {};
  volatile LONG ObserverReady[4096] = {};
  void ObserverEnter(FunctionIDOrClientID id);
}
class Observer final : public ICorProfilerCallback2 {
  LONG refs = 1;
public:
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** out) override {
    if (!out) return E_POINTER; *out = nullptr;
    if (iid==__uuidof(IUnknown) || iid==__uuidof(ICorProfilerCallback) || iid==__uuidof(ICorProfilerCallback2)) { *out=static_cast<ICorProfilerCallback2*>(this); AddRef(); return S_OK; } return E_NOINTERFACE;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
  ULONG STDMETHODCALLTYPE Release() override { const auto count=InterlockedDecrement(&refs); if(!count) delete this; return count; }
  HRESULT STDMETHODCALLTYPE Initialize(IUnknown* unknown) override {
    wchar_t path[32768];
    const DWORD n=GetEnvironmentVariableW(L"STROGO_OBSERVER_TRACE",path,32768);
    const DWORD a=GetEnvironmentVariableW(L"STROGO_OBSERVER_TYPE",targetType,256);
    const DWORD b=GetEnvironmentVariableW(L"STROGO_OBSERVER_METHOD",targetMethod,256);
    if(!n || n>=32768 || !a || a>=256 || !b || b>=256) return E_INVALIDARG;
    trace=CreateFileW(path,GENERIC_WRITE,FILE_SHARE_READ,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(trace==INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
    HRESULT hr=unknown->QueryInterface(__uuidof(ICorProfilerInfo3),reinterpret_cast<void**>(&info));
    if(SUCCEEDED(hr)) hr=info->SetEventMask(static_cast<DWORD>(COR_PRF_MONITOR_ENTERLEAVE)|static_cast<DWORD>(COR_PRF_DISABLE_INLINING)|static_cast<DWORD>(COR_PRF_DISABLE_ALL_NGEN_IMAGES));
    if(SUCCEEDED(hr)) hr=info->SetFunctionIDMapper2(Map,nullptr);
    if(SUCCEEDED(hr)) hr=info->SetEnterLeaveFunctionHooks3(ObserverEnter,nullptr,nullptr);
    char line[160]; sprintf_s(line,"{\"event\":\"initialize\",\"pid\":%lu,\"hresult\":%ld}\n",GetCurrentProcessId(),hr); Write(line);
    if(SUCCEEDED(hr)) InterlockedExchange(&ObserverActive,1);
    else {
      if(info) { info->Release(); info=nullptr; }
      CloseHandle(trace); trace=INVALID_HANDLE_VALUE;
    }
    return hr;
  }
  HRESULT STDMETHODCALLTYPE Shutdown() override {
    InterlockedExchange(&ObserverActive,0);
    AcquireSRWLockExclusive(&lifecycleLock);
    traceFailed = traceFailed || InterlockedCompareExchange(&ObserverOverflow,0,0)!=0 || InterlockedCompareExchange(&ObserverHooks,0,0)!=0;
    const LONG64 count=InterlockedCompareExchange64(&ObserverCount,0,0);
    entries=static_cast<unsigned>(count > 4096 ? 4096 : count);
    if(!traceFailed) for(unsigned i=0;i<entries;++i) {
      if(InterlockedCompareExchange(&ObserverReady[i],0,0)!=1){traceFailed=true;break;}
      char row[160];sprintf_s(row,"{\"event\":\"enter\",\"pid\":%lu,\"functionId\":\"%llu\"}\n",GetCurrentProcessId(),static_cast<unsigned long long>(ObserverSlots[i]));Write(row);
    }
    char line[180]; sprintf_s(line,"{\"event\":\"shutdown\",\"pid\":%lu,\"entries\":%u,\"traceFailed\":%s}\n",GetCurrentProcessId(),entries,traceFailed?"true":"false"); Write(line);
    if(info) {info->Release(); info=nullptr;} if(trace!=INVALID_HANDLE_VALUE) {CloseHandle(trace);trace=INVALID_HANDLE_VALUE;} ReleaseSRWLockExclusive(&lifecycleLock); return S_OK;
  }
#include "CallbackStubs.inc"
};
class Factory final : public IClassFactory {
  LONG refs=1;
public:
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** out) override { if(!out) return E_POINTER; *out=nullptr; if(iid==__uuidof(IUnknown)||iid==__uuidof(IClassFactory)){*out=this;AddRef();return S_OK;} return E_NOINTERFACE; }
  ULONG STDMETHODCALLTYPE AddRef() override {return InterlockedIncrement(&refs);}
  ULONG STDMETHODCALLTYPE Release() override {auto n=InterlockedDecrement(&refs);if(!n)delete this;return n;}
  HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,REFIID iid,void** out) override {if(outer)return CLASS_E_NOAGGREGATION;auto value=new Observer;auto hr=value->QueryInterface(iid,out);value->Release();return hr;}
  HRESULT STDMETHODCALLTYPE LockServer(BOOL) override {return S_OK;}
};
STDAPI DllGetClassObject(REFCLSID clsid,REFIID iid,void** out) {if(clsid!=ObserverId)return CLASS_E_CLASSNOTAVAILABLE;auto value=new Factory;auto hr=value->QueryInterface(iid,out);value->Release();return hr;}
STDAPI DllCanUnloadNow() {return S_FALSE;}
