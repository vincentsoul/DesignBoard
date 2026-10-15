-- Appended to xMain.lua by the local APK builder. Original assets stay local.
-- Replace the transport at NetMgr; retain the game's login response handlers.
do
    local baseUrl = "http://127.0.0.1:4317"
    local token = nil
    local pending = {}
    local busy = false
    local UnityWebRequest = CS.UnityEngine.Networking.UnityWebRequest
    local function record(text)
        CS.UnityEngine.Debug.LogWarning("[TrailLocal] " .. text)
        local file = io.open(CS.UnityEngine.Application.persistentDataPath .. "/trail-local.log", "a")
        if file then file:write(os.time() .. " " .. text .. "\n"); file:close() end
    end
    local function status(text)
        record(text)
        if CS.TestLoginScene.Loaded then
            local login = CS.TestLoginScene.Me
            if login.loadingStateText ~= nil then login.loadingStateText.text = text end
        end
    end
    local function pump()
        if busy or #pending == 0 then return end
        busy = true
        local entry = table.remove(pending, 1)
        coroutine.start(function()
            local ok, err = xpcall(function()
                -- Post(WWWForm) is present in this build's xLua bindings; Put is stripped.
                local form = CS.UnityEngine.WWWForm()
                form:AddField("message", json.encode({command = entry.command, payload = entry.payload}))
                local request = UnityWebRequest.Post(baseUrl .. "/api/client/message", form)
                request.timeout = 10
                if token ~= nil then request:SetRequestHeader("Authorization", "Bearer " .. token) end
                request:SendWebRequest()
                while not request.isDone do coroutine.waitforseconds(0.05) end
                local code = request.responseCode
                local text = request.downloadHandler.text
                request:Dispose()
                if code ~= 200 then
                    status("本地服务请求失败: " .. entry.command .. " / " .. code)
                    return
                end
                local response = json.decode(text)
                assert(response.ok and response.result, "Invalid local response")
                if response.result.token then token = response.result.token end
                for _, message in ipairs(response.result.messages) do
                    record("received " .. message.command)
                    entry.owner:OnRecv(message.command, message.payload)
                    record("handled " .. message.command)
                end
            end, debug.traceback)
            if not ok then status("本地适配错误: " .. tostring(err)) end
            busy = false
            pump()
        end)
    end
    function NetMgr:Send(command, payload)
        table.insert(pending, {owner = self, command = command, payload = payload})
        pump()
    end
    function NetMgr:InitConnection() end
    function NetMgr:OnTick() end
    function NetMgr:CloseConnect()
        pending = {}
        self:SetState(Const.NetState.ST_DISCONN)
    end
    function NetMgr:ConnectServer()
        self:SetState(Const.NetState.ST_CONNECTED)
        local player = PlayerData:GetInstance()
        player.login_info = ""
        player.log_info = ""
        player.account_name = "local-apk-guest"
        player.udid = "local-apk-guest"
        ServerData:GetInstance().server_id = 1
        status("正在连接本地服务器…")
        self:Send(C_LOGIN_AUTH, {account_name = player.account_name, sdkuid = "local-apk-guest"})
    end
    -- Prevent the original login scene from waiting for the remote SDK/server list.
    xlua.private_accessible(CS.TestLoginScene)
    xlua.hotfix(CS.TestLoginScene, "Start", function(self)
        self.gotServer = true
        self.sdkLoginSuc = true
        self.sdkLoginAlready = true
        self.devServerIP = "127.0.0.1"
        self.devServerPort = 4317
        self.devServerName = "本地测试"
        coroutine.start(function()
            coroutine.waitforseconds(1)
            NetMgr:GetInstance():ConnectServer()
        end)
    end)
    record("transport installed")
end
