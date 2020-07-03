// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "IOEvent.hpp"
#include "BuildXLException.hpp"

#if __APPLE__
IOEvent::IOEvent(const es_message_t *msg)
{
    pid_ = audit_token_to_pid(msg->process->audit_token);
    cpid_ = 0;
    ppid_ = msg->process->ppid;
    oppid_ = msg->process->original_ppid;
    eventType_ = msg->event_type;
    modified_ = false;
    
    executable_ = PathExtractor(msg->process->executable).Path();
    auditToken_ = msg->process->audit_token;
    
    switch (eventType_)
    {
        case ES_EVENT_TYPE_NOTIFY_EXEC: {
            ES_EVENT_CONSTRUCTOR(exec, "", target->executable, false, true)
        }
        case ES_EVENT_TYPE_NOTIFY_OPEN: {
            ES_EVENT_CONSTRUCTOR(open, "", file, false, true)
        }
        case ES_EVENT_TYPE_NOTIFY_FORK:
        {
            es_event_fork_t fork = msg->event.fork;
            executable_ = PathExtractor(fork.child->executable).Path();
            cpid_ = audit_token_to_pid(fork.child->audit_token);
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_CLOSE: {
            ES_EVENT_CONSTRUCTOR(close, "", target, true, false)
            modified_ = event.modified;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_CREATE: {
            es_event_create_t create = msg->event.create;
            bool existingFile = create.destination_type == ES_DESTINATION_TYPE_EXISTING_FILE;
            
            if (existingFile)
            {
                src_path_ = PathExtractor(create.destination.existing_file).Path();
                mode_ = create.destination.existing_file->stat.st_mode;
            }
            else
            {
                src_path_ = PathExtractor(create.destination.new_path.dir, create.destination.new_path.filename).Path();
                mode_ = create.destination.new_path.mode;
            }
            
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA: {
            es_event_exchangedata_t exchange = msg->event.exchangedata;
            src_path_ = PathExtractor(exchange.file1).Path();
            dst_path_ = PathExtractor(exchange.file2).Path();
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_EXIT:
        {
            // Nothing else to do
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_LINK: {
            es_event_link_t link = msg->event.link;
            src_path_ = PathExtractor(link.source).Path();
            dst_path_ = PathExtractor(link.target_dir, link.target_filename).Path();
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_RENAME: {
            es_event_rename_t rename = msg->event.rename;
            src_path_ = PathExtractor(rename.source).Path();
            
            bool existingFile = rename.destination_type == ES_DESTINATION_TYPE_EXISTING_FILE;
            if (existingFile)
            {
                dst_path_ = PathExtractor(rename.destination.existing_file).Path();
                mode_ = rename.destination.existing_file->stat.st_mode;
            }
            else
            {
                dst_path_ = PathExtractor(rename.destination.new_path.dir, rename.destination.new_path.filename).Path();
                mode_ = 0;
            }
            
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_SETATTRLIST: {
            ES_EVENT_CONSTRUCTOR(setattrlist, "", target, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_SETEXTATTR: {
            ES_EVENT_CONSTRUCTOR(setextattr, "", target, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_SETFLAGS: {
            ES_EVENT_CONSTRUCTOR(setflags, "", target, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_SETMODE: {
            ES_EVENT_CONSTRUCTOR(setmode, "", target, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_SETOWNER: {
            ES_EVENT_CONSTRUCTOR(setowner, "", target, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_UNLINK: {
            ES_EVENT_CONSTRUCTOR(unlink, "", target, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_WRITE: {
            ES_EVENT_CONSTRUCTOR(write, "", target, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_READLINK: {
            ES_EVENT_CONSTRUCTOR(readlink, "", source, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_TRUNCATE: {
            ES_EVENT_CONSTRUCTOR(truncate, "", target, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_LOOKUP: {
            es_event_lookup_t lookup = msg->event.lookup;
            src_path_ = PathExtractor(lookup.source_dir, lookup.relative_target).Path();
            mode_ = lookup.source_dir->stat.st_mode;
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_CHDIR: {
            ES_EVENT_CONSTRUCTOR(chdir, "", target, true, true)
        }
        case ES_EVENT_TYPE_NOTIFY_GETATTRLIST: {
            ES_EVENT_CONSTRUCTOR(getattrlist, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_STAT: {
            ES_EVENT_CONSTRUCTOR(stat, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_ACCESS: {
            ES_EVENT_CONSTRUCTOR(access, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_UTIMES: {
            ES_EVENT_CONSTRUCTOR(utimes, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_CLONE: {
            es_event_clone_t clone = msg->event.clone;
            src_path_ = PathExtractor(clone.source).Path();
            dst_path_ = PathExtractor(clone.target_dir, clone.target_name).Path();
            break;
        }
        case ES_EVENT_TYPE_NOTIFY_FCNTL: {
            ES_EVENT_CONSTRUCTOR(fcntl, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_GETEXTATTR: {
            ES_EVENT_CONSTRUCTOR(getextattr, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_LISTEXTATTR: {
            ES_EVENT_CONSTRUCTOR(listextattr, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_READDIR: {
            ES_EVENT_CONSTRUCTOR(readdir, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR: {
            ES_EVENT_CONSTRUCTOR(deleteextattr, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_FSGETPATH: {
            ES_EVENT_CONSTRUCTOR(fsgetpath, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_DUP: {
            ES_EVENT_CONSTRUCTOR(dup, "", target, true, true);
        }
        case ES_EVENT_TYPE_NOTIFY_SETACL: {
            ES_EVENT_CONSTRUCTOR(setacl, "", target, true, true);
        }
        default: {
            throw BuildXLException("Failed to transform ES message to IOEvent - no mapping for type: " + std::to_string(msg->event_type));
        }
    }
}
#endif

// When inserting the detours library dynamically, interposed executables automatically search for the default Info.plist
// file in the executable directory, we are ignoring these events because they are triggered by the interposing and normally don't happen!
const bool IOEvent::IsPlistEvent() const
{
    size_t match = src_path_.find("Info.plist");
    if (match != std::string::npos)
    {
        size_t directory = executable_.find_last_of("/");
        if (directory != std::string::npos)
        {
            std::string path = src_path_.substr(0, match);
            std::string base_path = executable_.substr(0, directory + 1);
            
            return base_path.compare(path) == 0;
        }
    }
    
    return false;
}

// Ignore events that refer to the directory special characters '.' and '..'
const bool IOEvent::IsDirectorySpecialCharacterEvent() const
{
    return src_path_.compare(".") == 0 || src_path_.compare("..") == 0;
}

const size_t IOEvent::Size() const
{
    return
        std::to_string(pid_).length() +
        std::to_string(cpid_).length() +
        std::to_string(ppid_).length() +
        std::to_string(eventType_).length() +
        std::to_string(mode_).length() +
        std::to_string(modified_).length() +
        executable_.length() + (executable_.length() > 0 ? 1 : 0) +
        src_path_.length() + (src_path_.length() > 0 ? 1 : 0) +
        dst_path_.length() + (dst_path_.length() > 0 ? 1 : 0) +
        6; // 6 delimiters + 1 per string (if string is present)
}

omemorystream& operator<<(omemorystream &os, const IOEvent &event)
{
    os
    << event.pid_       << "|"
    << event.cpid_      << "|"
    << event.ppid_      << "|"
    << event.eventType_ << "|"
    << event.mode_      << "|"
    << event.modified_  << "|"
    ;
    
    if (event.executable_.size() > 0)
    {
        os << event.executable_ << "|";
    }
    
    if (event.src_path_.size() > 0)
    {
        os << event.src_path_ << "|";
    }
    
    if (event.dst_path_.size() > 0)
    {
        os << event.dst_path_ << "|";
    }

    return os;
}

std::istream& operator>>(std::istream &is, es_event_type_t &t)
{
    unsigned int value = 0;
    is >> value;
    t = (es_event_type_t) value;
    return is;
}

imemorystream& operator>>(imemorystream &is, IOEvent &event)
{
    is
    >> event.pid_
    >> event.cpid_
    >> event.ppid_
    >> event.eventType_
    >> event.mode_
    >> event.modified_
    >> event.executable_
    >> event.src_path_
    >> event.dst_path_
    ;
    
    return is;
}
