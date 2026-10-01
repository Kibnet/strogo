// Diagnostic local-file evidence only; not a mapped-memory hash.
#include <bcrypt.h>
#pragma comment(lib,"bcrypt.lib")
static bool fileIdentityEnabled=false;
struct ModuleFileRecord { ModuleID module; HANDLE handle; ULONGLONG length; DWORD volume, high, low; char digest[65]; bool available; };
static ModuleFileRecord moduleFiles[32]={};
static unsigned moduleFileCount=0;
static SRWLOCK moduleFileLock=SRWLOCK_INIT;
static bool LocalFileDigest(const wchar_t* path, ModuleFileRecord& record) {
  wchar_t root[4]={path[0],L':',L'\\',0};
  if(!((path[0]>=L'A'&&path[0]<=L'Z')||(path[0]>=L'a'&&path[0]<=L'z')) || path[1]!=L':' || path[2]!=L'\\' || wcschr(path+2,L':') || GetDriveTypeW(root)!=DRIVE_FIXED) return false;
  record.handle=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_FLAG_OPEN_REPARSE_POINT,nullptr);
  if(record.handle==INVALID_HANDLE_VALUE) return false;
  BCRYPT_ALG_HANDLE algorithm=nullptr; BCRYPT_HASH_HANDLE hash=nullptr;
  BY_HANDLE_FILE_INFORMATION identity={}; LARGE_INTEGER size={}; BYTE buffer[4096], digest[32]; wchar_t finalPath[32768]={};
  DWORD finalChars=0;
  const ULONGLONG started=GetTickCount64(); ULONGLONG total=0; bool ok=false;
  if(GetFileType(record.handle)!=FILE_TYPE_DISK || !GetFileInformationByHandle(record.handle,&identity) ||
      (identity.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT)) || !GetFileSizeEx(record.handle,&size) || size.QuadPart<=0 || size.QuadPart>16777216) goto cleanup;
  finalChars=GetFinalPathNameByHandleW(record.handle,finalPath,32768,FILE_NAME_NORMALIZED|VOLUME_NAME_DOS);
  if(!finalChars || finalChars>=32768 || wcsncmp(finalPath,L"\\\\?\\",4) || finalPath[5]!=L':' || finalPath[6]!=L'\\') goto cleanup;
  root[0]=finalPath[4];if(GetDriveTypeW(root)!=DRIVE_FIXED) goto cleanup;
  if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0 || BCryptCreateHash(algorithm,&hash,nullptr,0,nullptr,0,0)<0) goto cleanup;
  while(total<static_cast<ULONGLONG>(size.QuadPart)) {
    DWORD read=0;const DWORD wanted=static_cast<DWORD>((static_cast<ULONGLONG>(size.QuadPart)-total)>sizeof(buffer)?sizeof(buffer):(static_cast<ULONGLONG>(size.QuadPart)-total));
    if(GetTickCount64()-started>2000 || !ReadFile(record.handle,buffer,wanted,&read,nullptr) || read!=wanted || GetTickCount64()-started>2000 || BCryptHashData(hash,buffer,read,0)<0) goto cleanup;
    total+=read;
  }
  if(BCryptFinishHash(hash,digest,sizeof(digest),0)<0) goto cleanup;
  if(GetTickCount64()-started>2000) goto cleanup;
  for(unsigned i=0;i<32;++i) sprintf_s(record.digest+i*2,65-i*2,"%02x",digest[i]);
  record.length=total;record.volume=identity.dwVolumeSerialNumber;record.high=identity.nFileIndexHigh;record.low=identity.nFileIndexLow;ok=true;
cleanup:
  if(hash) BCryptDestroyHash(hash);if(algorithm) BCryptCloseAlgorithmProvider(algorithm,0);
  if(!ok){CloseHandle(record.handle);record.handle=INVALID_HANDLE_VALUE;}
  return ok;
}
static bool RecordModuleFile(ModuleID module, const wchar_t* path, FunctionID function) {
  ModuleFileRecord record={};record.module=module;record.handle=INVALID_HANDLE_VALUE;
  AcquireSRWLockExclusive(&moduleFileLock);
  // Do not reuse evidence by ModuleID alone: unloaded module identifiers can be reused.
  if(moduleFileCount<32) {record.available=LocalFileDigest(path,record);moduleFiles[moduleFileCount++]=record;}
  ReleaseSRWLockExclusive(&moduleFileLock);
  char line[512];sprintf_s(line,"{\"event\":\"module-file\",\"functionId\":\"%llu\",\"pid\":%lu,\"moduleId\":\"%llu\",\"available\":%s,\"length\":%llu,\"sha256\":\"%s\",\"volume\":%lu,\"fileIdHigh\":%lu,\"fileIdLow\":%lu}\n",static_cast<unsigned long long>(function),GetCurrentProcessId(),static_cast<unsigned long long>(module),record.available?"true":"false",record.length,record.digest,record.volume,record.high,record.low);Write(line);
  if(!record.available){AcquireSRWLockExclusive(&traceLock);traceFailed=true;ReleaseSRWLockExclusive(&traceLock);}
  return record.available;
}
static void CloseModuleFiles() { for(unsigned i=0;i<moduleFileCount;++i) if(moduleFiles[i].handle!=INVALID_HANDLE_VALUE) CloseHandle(moduleFiles[i].handle); }
