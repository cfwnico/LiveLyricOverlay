#include "stdafx.h"
#include <string>
#include <thread>
#include <mutex>
#include <atomic>

DECLARE_COMPONENT_VERSION("LiveLyricOverlay Sync", "1.0", "Sends playback state to LiveLyricOverlay via Named Pipes");

// Simple JSON builder
std::string BuildJsonMsg(const char* eventType, double time, const char* filePath) {
    char buf[1024];
    snprintf(buf, sizeof(buf), "{\"EventType\":\"%s\",\"Time\":%.3f,\"FilePath\":\"%s\"}\n", eventType, time, filePath);
    return std::string(buf);
}

class IPCServer {
public:
    IPCServer() : m_thread(&IPCServer::Worker, this) {}
    ~IPCServer() {
        m_stop = true;
        // Connect to unblock WaitNamedPipe
        CallNamedPipeA("\\\\.\\pipe\\LiveLyricOverlayPipe", nullptr, 0, nullptr, 0, nullptr, 1);
        if (m_thread.joinable()) m_thread.join();
    }

    void SendEvent(const std::string& msg) {
        std::lock_guard<std::mutex> lock(m_mutex);
        if (m_connected && m_pipe != INVALID_HANDLE_VALUE) {
            DWORD written = 0;
            WriteFile(m_pipe, msg.c_str(), msg.length(), &written, nullptr);
            FlushFileBuffers(m_pipe);
        }
    }

private:
    void Worker() {
        while (!m_stop) {
            m_pipe = CreateNamedPipeA("\\\\.\\pipe\\LiveLyricOverlayPipe",
                PIPE_ACCESS_OUTBOUND,
                PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
                1, 4096, 4096, 0, nullptr);
            
            if (m_pipe == INVALID_HANDLE_VALUE) {
                Sleep(1000);
                continue;
            }

            if (ConnectNamedPipe(m_pipe, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED) {
                m_connected = true;
                // Wait until pipe breaks
                while (!m_stop && m_connected) {
                    Sleep(100);
                    DWORD dummy;
                    if (!GetNamedPipeInfo(m_pipe, nullptr, nullptr, nullptr, &dummy)) {
                        m_connected = false;
                    }
                }
            }
            m_connected = false;
            CloseHandle(m_pipe);
            m_pipe = INVALID_HANDLE_VALUE;
        }
    }

    std::thread m_thread;
    std::mutex m_mutex;
    std::atomic<bool> m_stop{false};
    std::atomic<bool> m_connected{false};
    HANDLE m_pipe = INVALID_HANDLE_VALUE;
};

IPCServer g_ipc;

class play_callback_livelyric : public play_callback_static {
public:
    unsigned get_flags() override {
        return flag_on_playback_new_track | flag_on_playback_starting | flag_on_playback_pause | flag_on_playback_stop | flag_on_playback_seek;
    }

    void on_playback_new_track(metadb_handle_ptr p_track) override {
        pfc::string8 path = p_track->get_path();
        if (strncmp(path.get_ptr(), "file://", 7) == 0) path = path.get_ptr() + 7;
        std::string lrcPath = std::string(path.get_ptr());
        
        size_t dot = lrcPath.find_last_of('.');
        if (dot != std::string::npos) lrcPath = lrcPath.substr(0, dot) + ".lrc";
        else lrcPath += ".lrc";
        
        std::string escaped;
        for (char c : lrcPath) {
            if (c == '\\') escaped += "\\\\";
            else escaped += c;
        }

        g_ipc.SendEvent(BuildJsonMsg("new_track", 0.0, escaped.c_str()));
    }

    void on_playback_starting(play_control::t_track_command p_command, bool p_paused) override {
        if (!p_paused) {
            g_ipc.SendEvent(BuildJsonMsg("play", 0.0, ""));
        }
    }
    
    void on_playback_pause(bool p_state) override {
        double pos = play_control::get()->playback_get_position();
        if (p_state) {
            g_ipc.SendEvent(BuildJsonMsg("pause", pos, ""));
        } else {
            g_ipc.SendEvent(BuildJsonMsg("play", pos, ""));
        }
    }
    
    void on_playback_stop(play_control::t_stop_reason p_reason) override {
        g_ipc.SendEvent(BuildJsonMsg("stop", 0.0, ""));
    }
    
    void on_playback_seek(double p_time) override {
        g_ipc.SendEvent(BuildJsonMsg("seek", p_time, ""));
    }
    
    void on_playback_time(double p_time) override {}
    void on_playback_dynamic_info(const file_info & p_info) override {}
    void on_playback_dynamic_info_track(const file_info & p_info) override {}
    void on_playback_edited(metadb_handle_ptr p_track) override {}
    void on_volume_change(float p_new_val) override {}
};

static play_callback_static_factory_t<play_callback_livelyric> g_play_callback;
