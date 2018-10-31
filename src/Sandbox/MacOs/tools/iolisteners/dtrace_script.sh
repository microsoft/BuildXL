#!/bin/bash

done=false;
exitCode=0;
failures=0;

while [ $done == false ]
do
	#listen for opens, writes, and reads and append to file passed as argument ($1)... with walltimestamp of each I/O event
	echo "--==Monitoring I/O with DTrace Script==--";
	#...................................................
	# There are several choices here that need explaining
	#	(1)	We have to separate the scripts for the open probes and the read/write probs.
	#		
	#		(1.1) This is because write/reads often cause errors with copyinstr because copyinstr() subroutines cannot read from user addresses which have not yet been touched... so rather we rely on fds, which
	#		is a structure holding file description information. However..., this structure is incomplete in Mac OSX and replaces the mount path with '??', so these '??' have to be replaced later. We will do this by looking at 
	#		files that are opened previously and use that path.
	#		(1.2) Open probs do not play well with fds, however copyinstr seems to work just fine... so we use copyinstr to get the path of the file that is opened.	 
	#
	# 	(2)	Either root or sudo is assumed. For best performance with sudo, please require no password... (update /etc/sudoers by running "sudo visudo" and set "%admin all=(all) nopasswd: all")
	#		For sudo, you can enter the password at the prompt at start, but if there is a failure and you are not at keyboard to enter password again, then this script will miss I/O.
	#
	#	(3)	"/pid != $pid/" matches on processes other than DTrace. This helps not record any syscalls of DTrace itself and entering some cascade of events caused by DTrace itself.
	#
	#	(4)	SIP must be disabled... check "csrutil status" to see if your system has SIP disabled, otherwise Restart->command+R->open terminal->csrutil disable
	#...................................................		
	if [ $EUID -ne 0 ]; then
		echo "Running with SUDO"
		sudo dtrace -n 'syscall::*open*:entry /pid != $pid/{ printf("[%Y] %s %s", walltimestamp, execname, copyinstr(arg0)) }' -n 'syscall::*write*:entry, syscall::*read*:entry /pid != $pid/ { printf("[%Y] %s", walltimestamp, fds[arg0].fi_pathname) }' >> $1
	else
		echo "Running as root"
		dtrace -n 'syscall::*open*:entry /pid != $pid/{ printf("[%Y] %s %s", walltimestamp, execname, copyinstr(arg0)) }' -n 'syscall::*write*:entry, syscall::*read*:entry /pid != $pid/ { printf("[%Y] %s", walltimestamp, fds[arg0].fi_pathname) }' >> $1
	fi
	exitCode=$?;

	#............................
	# DTrace sometimes fails with:
	#	dtrace: processing aborted: Abort due to systemic unresponsiveness
	#	, so we check for this and start back up if it fails.
	#............................
	if [ $exitCode -ne 0 ]; then
		done=false;
		((failures++));
		echo "Restarting with [$failures] failures and exit code of [$exitCode].";
	else
		done=true;
	fi
done

echo "Completed with [$failures] failures and exit code of [$exitCode].";
