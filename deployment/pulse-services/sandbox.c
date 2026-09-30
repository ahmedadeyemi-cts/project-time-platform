/* Unprivileged, fail-closed process isolation for managed-container runtimes.
 * Does not request capabilities, a privileged container, or a kernel change. */
#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <linux/landlock.h>
#include <seccomp.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/prctl.h>
#include <sys/resource.h>
#include <sys/socket.h>
#include <sys/syscall.h>
#include <unistd.h>
#ifndef LANDLOCK_ACCESS_FS_TRUNCATE
#define LANDLOCK_ACCESS_FS_TRUNCATE (1ULL << 14)
#endif
static int path_rule(int rules, const char *path, uint64_t rights) {
    int fd = open(path, O_PATH | O_CLOEXEC);
    if (fd < 0) return -1;
    struct landlock_path_beneath_attr p = {.allowed_access=rights, .parent_fd=fd};
    int rc = syscall(SYS_landlock_add_rule, rules, LANDLOCK_RULE_PATH_BENEATH, &p, 0);
    close(fd); return rc;
}
int pulse_sandbox(const char *profile) {
    int scanner = !strcmp(profile,"scanner"), updater = !strcmp(profile,"updater");
    int laya = !strcmp(profile,"laya"), gateway = !strcmp(profile,"gateway");
    if ((!scanner && !updater && !laya && !gateway) || geteuid()==0) return -1;
    if (prctl(PR_SET_NO_NEW_PRIVS,1,0,0,0) || prctl(PR_SET_DUMPABLE,0,0,0,0)) return -1;
    struct rlimit core={0,0}; if(setrlimit(RLIMIT_CORE,&core)) return -1;
    int abi=syscall(SYS_landlock_create_ruleset,NULL,0,LANDLOCK_CREATE_RULESET_VERSION);
    if(abi<3) return -1; /* no silent downgrade when truncate is unsupported */
    uint64_t writes=LANDLOCK_ACCESS_FS_WRITE_FILE|LANDLOCK_ACCESS_FS_REMOVE_DIR|
        LANDLOCK_ACCESS_FS_REMOVE_FILE|LANDLOCK_ACCESS_FS_MAKE_DIR|LANDLOCK_ACCESS_FS_MAKE_REG|
        LANDLOCK_ACCESS_FS_MAKE_SOCK|LANDLOCK_ACCESS_FS_MAKE_FIFO|LANDLOCK_ACCESS_FS_MAKE_SYM|
        LANDLOCK_ACCESS_FS_REFER|LANDLOCK_ACCESS_FS_TRUNCATE;
    struct landlock_ruleset_attr a={.handled_access_fs=writes|LANDLOCK_ACCESS_FS_EXECUTE|
        LANDLOCK_ACCESS_FS_MAKE_CHAR|LANDLOCK_ACCESS_FS_MAKE_BLOCK};
    int fd=syscall(SYS_landlock_create_ruleset,&a,sizeof(a),0); if(fd<0) return -1;
    const char *execs[]={"/usr","/opt","/bin","/sbin","/lib","/lib64"};
    for(unsigned i=0;i<sizeof(execs)/sizeof(execs[0]);i++)
        if(access(execs[i],F_OK)==0 && path_rule(fd,execs[i],LANDLOCK_ACCESS_FS_EXECUTE)) {close(fd);return -1;}
    if(path_rule(fd,"/tmp",writes)||path_rule(fd,"/dev/null",LANDLOCK_ACCESS_FS_WRITE_FILE)) {close(fd);return -1;}
    if(scanner && path_rule(fd,"/run/clamav",writes)) {close(fd);return -1;}
    if(updater && path_rule(fd,"/var/lib/clamav",writes)) {close(fd);return -1;}
    if(laya && (path_rule(fd,"/run/celar-laya",writes)||path_rule(fd,"/dev/shm",writes))) {close(fd);return -1;}
    if(syscall(SYS_landlock_restrict_self,fd,0)) {close(fd);return -1;} close(fd);
    scmp_filter_ctx ctx=seccomp_init(SCMP_ACT_ALLOW); if(!ctx)return -1;
    const char *denied[]={"ptrace","process_vm_readv","process_vm_writev","mount","umount2","bpf","kexec_load","open_by_handle_at"};
    for(unsigned i=0;i<sizeof(denied)/sizeof(denied[0]);i++) {
        int nr=seccomp_syscall_resolve_name(denied[i]);
        if(nr!=__NR_SCMP_ERROR && seccomp_rule_add(ctx,SCMP_ACT_ERRNO(EPERM),nr,0)) {seccomp_release(ctx);return -1;}
    }
    /* HTTP listeners are created by the master before gateway workers apply this.
       Accept/send on inherited connections remain usable; new IP sockets cannot open. */
    if(!updater && seccomp_rule_add(ctx,SCMP_ACT_ERRNO(EPERM),SCMP_SYS(socket),1,
                                  SCMP_A0(SCMP_CMP_NE,AF_UNIX))) {seccomp_release(ctx);return -1;}
    if(seccomp_load(ctx)){seccomp_release(ctx);return -1;}seccomp_release(ctx);
    return 0;
}
#ifndef PULSE_SHARED
int main(int argc,char **argv) {
    if(argc<3 || pulse_sandbox(argv[1])) {
        fputs("PULSE_ISOLATION_UNAVAILABLE; activation denied\n",stderr);return 78;
    }
    execvp(argv[2],argv+2);fputs("PULSE_EXECUTION_FAILED\n",stderr);return 78;
}
#endif
