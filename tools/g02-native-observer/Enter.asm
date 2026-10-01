EXTERN ObserverCount:QWORD
EXTERN ObserverActive:DWORD
EXTERN ObserverHooks:DWORD
EXTERN ObserverOverflow:DWORD
EXTERN ObserverSlots:QWORD
EXTERN ObserverReady:DWORD
PUBLIC ObserverEnter
.code
; No calls, allocations, locks or I/O. Only RAX/R10/RFLAGS are changed and restored.
ObserverEnter PROC
 pushfq
 push rax
 push r10
 lock inc DWORD PTR ObserverHooks
 cmp DWORD PTR ObserverActive,0
 je done
 mov rax,1
 lock xadd QWORD PTR ObserverCount,rax
 cmp rax,4096
 jae overflow
 lea r10,ObserverSlots
 mov QWORD PTR [r10+rax*8],rcx
 lea r10,ObserverReady
 mov DWORD PTR [r10+rax*4],1
 jmp done
 overflow:
 mov DWORD PTR ObserverOverflow,1
 done:
 lock dec DWORD PTR ObserverHooks
 pop r10
 pop rax
 popfq
 ret
ObserverEnter ENDP
END
