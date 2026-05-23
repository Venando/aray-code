public enum CommandType
{
    Unknown,
    FileSystem,   // rm, cp, mv, mkdir, ls, cd, chmod, chown, find, touch
    FileContent,  // cat, echo, grep, sed, awk, head, tail, tee, diff, wc
    HereDoc,      // cat > file << 'DELIMITER' ... DELIMITER
    Process,      // kill, ps, top, nohup, bg, fg, jobs
    Network,      // curl, wget, ssh, scp, ping, netstat
    PackageManager, // apt, brew, npm, pip, dotnet, cargo
    Build,        // make, cmake, dotnet build/run/test, msbuild
    Pipe,         // compound with | 
    Redirect,     // compound with > / >>
    Chain,        // compound with && or ||
    Variable,     // export, env=value cmd
    Scripting,    // sh, bash, python, node, mono, dotnet-script
    Vcs,          // git, svn, hg
}
