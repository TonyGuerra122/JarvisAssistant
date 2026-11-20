/* J.A.R.V.I.S — HUD
 * Recebe mensagens do app WPF (window.chrome.webview) e desenha a interface.
 * Aberto fora do app (num navegador comum), roda em modo demonstração.
 */
(() => {
    "use strict";

    const $ = (id) => document.getElementById(id);
    const host = window.chrome && window.chrome.webview;
    const cfg = { userTitle: "senhor", city: "São Paulo", language: "pt" };

    // ------------------------------------------------------------ ponte

    function send(msg) {
        if (host) host.postMessage(msg);
        else Demo.onSend(msg);
    }

    function handle(msg) {
        switch (msg.type) {
            case "config":
                Object.assign(cfg, msg);
                Weather.start();
                break;
            case "boot": Boot.line(msg.id, msg.text, msg.status); break;
            case "bootDone": Boot.done(); break;
            case "state": setState(msg.state); break;
            case "level": Orb.level = msg.value; break;
            case "caption": Caption.show(msg.role, msg.text); break;
            case "confirm": Confirm.open(msg.id, msg.text); break;
            case "confirmClose": Confirm.close(msg.id); break;
            case "status": setStatus(msg); break;
            case "update": Update.show(msg.status, msg.version); break;
        }
    }

    if (host) host.addEventListener("message", (e) => handle(e.data));

    // ------------------------------------------------------------ estado

    const STATE_LABEL = {
        booting: "INICIANDO",
        idle: "EM ESPERA",
        listening: "OUVINDO",
        thinking: "PROCESSANDO",
        speaking: "FALANDO",
        offline: "NÚCLEO OFFLINE",
        confirm: "AGUARDANDO CONFIRMAÇÃO"
    };

    // ------------------------------------------------------------ atualização

    const Update = {
        show(status, version) {
            const bar = $("updateBar"), text = $("updateText"), btn = $("updateBtn");
            if (status === "downloading") {
                text.textContent = `BAIXANDO ATUALIZAÇÃO ${version}…`;
                btn.hidden = true;
                bar.hidden = false;
            } else if (status === "ready") {
                text.textContent = `ATUALIZAÇÃO ${version} PRONTA · INSTALA AO FECHAR`;
                btn.hidden = false;
                bar.hidden = false;
            } else {
                bar.hidden = true; // erro: tenta de novo mais tarde, sem incomodar
            }
        }
    };
    $("updateBtn").addEventListener("click", () => send({ type: "applyUpdate" }));

    const Confirm = {
        id: null,
        open(id, text) {
            this.id = id;
            $("confirmText").textContent = text;
            $("confirmBox").hidden = false;
        },
        close(id) {
            if (id !== undefined && id !== this.id) return;
            this.id = null;
            $("confirmBox").hidden = true;
        },
        answer(ok) {
            if (this.id === null) return;
            send({ type: "confirmReply", id: this.id, ok });
            this.close();
        },
        get isOpen() { return this.id !== null; },
    };

    $("confirmYes").addEventListener("click", () => Confirm.answer(true));
    $("confirmNo").addEventListener("click", () => Confirm.answer(false));

    function setState(state) {
        document.body.dataset.state = state;
        Orb.state = state;
        $("statePill").querySelector("span").textContent = STATE_LABEL[state] || state.toUpperCase();
    }

    function setStatus(s) {
        const core = $("sCore");
        core.textContent = s.online ? "ONLINE" : "OFFLINE";
        core.className = s.online ? "ok" : "fail";
        const tag = $("coreTag");
        tag.textContent = s.online ? "OK" : "FALHA";
        tag.className = "tag " + (s.online ? "on" : "off");
        $("sModel").textContent = s.model;
        $("sModel").title = s.model;
        const listen = $("sListen");
        listen.textContent = s.listen ? "ATIVA" : "INDISPONÍVEL";
        listen.className = s.listen ? "ok" : "fail";
        $("sVoice").textContent = s.voice;
        $("sVoice").title = s.voice;
    }

    // ------------------------------------------------------------ boot

    const Boot = {
        lines: new Map(),
        line(id, text, status) {
            let el = this.lines.get(id);
            if (!el) {
                el = document.createElement("div");
                el.innerHTML = '<span class="t"></span><span class="s"></span>';
                $("bootLines").appendChild(el);
                this.lines.set(id, el);
            }
            el.className = "bline " + status;
            el.querySelector(".t").textContent = text;
            el.querySelector(".s").textContent = status === "ok" ? "OK" : status === "fail" ? "FALHA" : "";
        },
        done() {
            if (document.body.classList.contains("ready")) return;
            const hasFail = [...this.lines.values()].some((l) => l.classList.contains("fail"));
            setTimeout(() => {
                $("boot").classList.add("hide");
                document.body.classList.remove("booting");
                document.body.classList.add("ready");
            }, hasFail ? 2600 : 900); // com falhas, dá tempo de ler
        },
    };

    // ------------------------------------------------------------ legendas

    const Caption = {
        timer: null,
        hideTimer: null,
        show(role, text) {
            const box = $("caption");
            const who = { jarvis: "J.A.R.V.I.S", user: "VOCÊ", system: "SISTEMA" }[role] || "";
            box.className = role;
            box.querySelector(".who").textContent = who;
            const out = box.querySelector(".text");
            clearInterval(this.timer);
            clearTimeout(this.hideTimer);

            if (role === "jarvis") { // efeito de digitação
                let i = 0;
                out.textContent = "";
                const step = Math.max(1, Math.round(text.length / 120));
                this.timer = setInterval(() => {
                    i = Math.min(text.length, i + step);
                    out.textContent = text.slice(0, i);
                    if (i >= text.length) clearInterval(this.timer);
                }, 22);
            } else {
                out.textContent = text;
            }
            Activity.add(role, who, text);
        },
    };

    const Activity = {
        add(role, who, text) {
            const ul = $("activity");
            const li = document.createElement("li");
            li.className = role;
            const w = document.createElement("span");
            w.className = "who";
            w.textContent = who;
            li.append(w, document.createTextNode(text));
            ul.prepend(li);
            while (ul.children.length > 5) ul.lastChild.remove();
        },
    };

    // ------------------------------------------------------------ relógio

    function tick() {
        const now = new Date();
        $("time").textContent = now.toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
        const day = now.toLocaleDateString("pt-BR", { weekday: "long" });
        const date = now.toLocaleDateString("pt-BR");
        const temp = Weather.current ? ` · ${Weather.current}` : "";
        $("date").textContent = `${day} · ${date} · ${cfg.city}${temp}`;
    }

    // ------------------------------------------------------------ clima (Open-Meteo, sem chave)

    const WMO = {
        0: "Céu limpo", 1: "Predominantemente limpo", 2: "Parcialmente nublado", 3: "Nublado",
        45: "Neblina", 48: "Neblina", 51: "Garoa fraca", 53: "Garoa", 55: "Garoa forte",
        56: "Garoa congelante", 57: "Garoa congelante", 61: "Chuva fraca", 63: "Chuva", 65: "Chuva forte",
        66: "Chuva congelante", 67: "Chuva congelante", 71: "Neve fraca", 73: "Neve", 75: "Neve forte",
        77: "Grãos de neve", 80: "Pancadas fracas", 81: "Pancadas de chuva", 82: "Pancadas fortes",
        85: "Neve", 86: "Neve forte", 95: "Trovoadas", 96: "Trovoadas com granizo", 99: "Trovoadas com granizo",
    };

    const Weather = {
        current: "",
        started: false,
        place: null,
        start() {
            if (this.started) return;
            this.started = true;
            this.refresh();
            setInterval(() => this.refresh(), 15 * 60 * 1000);
        },
        async refresh() {
            try {
                if (!this.place) {
                    const q = encodeURIComponent(cfg.city);
                    const g = await (await fetch(`https://geocoding-api.open-meteo.com/v1/search?name=${q}&count=1&language=pt&format=json`)).json();
                    if (!g.results || !g.results.length) throw new Error("cidade não encontrada");
                    this.place = g.results[0];
                }
                const { latitude, longitude } = this.place;
                const url = `https://api.open-meteo.com/v1/forecast?latitude=${latitude}&longitude=${longitude}` +
                    "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m" +
                    "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";
                const w = await (await fetch(url)).json();
                const c = w.current;
                const desc = WMO[c.weather_code] || "—";
                const t = Math.round(c.temperature_2m);
                $("wCity").textContent = this.place.name;
                $("wTemp").textContent = `${t}°`;
                $("wDesc").textContent = desc;
                $("wMinMax").textContent = `MÍN ${Math.round(w.daily.temperature_2m_min[0])}° · MÁX ${Math.round(w.daily.temperature_2m_max[0])}°`;
                $("wHum").textContent = `${c.relative_humidity_2m}%`;
                $("wWind").textContent = `${Math.round(c.wind_speed_10m)} km/h`;
                $("wFeel").textContent = `${Math.round(c.apparent_temperature)}°`;
                this.current = `${t}°C`;
                send({
                    type: "weather",
                    summary: `${t} °C, ${desc.toLowerCase()}, sensação de ${Math.round(c.apparent_temperature)} °C, ` +
                        `umidade de ${c.relative_humidity_2m}%, mínima de ${Math.round(w.daily.temperature_2m_min[0])} °C ` +
                        `e máxima de ${Math.round(w.daily.temperature_2m_max[0])} °C hoje`,
                });
            } catch (err) {
                $("wDesc").textContent = "Sem dados de clima";
                console.warn("clima:", err);
            }
            tick();
        },
    };

    // ------------------------------------------------------------ esfera

    const Orb = {
        canvas: $("orb"),
        ctx: null,
        points: [],
        state: "booting",
        level: 0,
        smooth: 0,
        rot: 0,
        hue: 190,
        w: 0, h: 0, dpr: 1,

        init() {
            this.ctx = this.canvas.getContext("2d");
            const N = 1100;
            const golden = Math.PI * (3 - Math.sqrt(5));
            for (let i = 0; i < N; i++) { // esfera de Fibonacci
                const y = 1 - (i / (N - 1)) * 2;
                const r = Math.sqrt(1 - y * y);
                const t = golden * i;
                this.points.push({ x: Math.cos(t) * r, y, z: Math.sin(t) * r, phase: Math.random() * 6.28 });
            }
            addEventListener("resize", () => this.resize());
            this.resize();
            requestAnimationFrame((t) => this.frame(t));
        },

        resize() {
            this.dpr = Math.min(window.devicePixelRatio || 1, 2);
            this.w = innerWidth;
            this.h = innerHeight;
            this.canvas.width = this.w * this.dpr;
            this.canvas.height = this.h * this.dpr;
            this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0);
        },

        frame(ms) {
            const t = ms / 1000;
            const { ctx, w, h } = this;
            const st = this.state;

            // suaviza o volume: sobe rápido, desce devagar
            const target = st === "speaking" || st === "listening" ? this.level : 0;
            this.smooth += (target - this.smooth) * (target > this.smooth ? 0.45 : 0.08);
            const lv = this.smooth;

            const hueTarget = { idle: 190, listening: 160, thinking: 215, speaking: 188, offline: 350, booting: 195, confirm: 40 }[st] ?? 190;
            this.hue += (hueTarget - this.hue) * 0.05;
            const hue = st === "thinking" ? this.hue + Math.sin(t * 3) * 25 : this.hue;
            const sat = st === "offline" ? 55 : 100;

            const speed = st === "thinking" ? 0.022 : st === "speaking" ? 0.009 + lv * 0.02 : 0.005;
            this.rot += speed;

            const cx = w / 2;
            const cy = h * 0.44;
            const breathe = Math.sin(t * 1.4) * 0.015;
            const R = Math.min(w, h) * 0.165 * (1 + breathe + lv * 0.22);

            ctx.clearRect(0, 0, w, h);
            ctx.globalCompositeOperation = "lighter";

            // halo externo + brilho interno
            const glowA = 0.32 + lv * 0.55 + (st === "thinking" ? 0.1 : 0);
            const halo = ctx.createRadialGradient(cx, cy, R * 0.6, cx, cy, R * 2.1);
            halo.addColorStop(0, `hsla(${hue}, ${sat}%, 60%, ${0.10 + lv * 0.18})`);
            halo.addColorStop(1, `hsla(${hue}, ${sat}%, 50%, 0)`);
            ctx.fillStyle = halo;
            ctx.beginPath();
            ctx.arc(cx, cy, R * 2.1, 0, Math.PI * 2);
            ctx.fill();

            const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, R * 1.2);
            g.addColorStop(0, `hsla(${hue}, 100%, 96%, ${glowA})`);
            g.addColorStop(0.18, `hsla(${hue}, 100%, 80%, ${glowA * 0.75})`);
            g.addColorStop(0.5, `hsla(${hue}, ${sat}%, 60%, ${glowA * 0.3})`);
            g.addColorStop(1, `hsla(${hue}, ${sat}%, 50%, 0)`);
            ctx.fillStyle = g;
            ctx.beginPath();
            ctx.arc(cx, cy, R * 1.2, 0, Math.PI * 2);
            ctx.fill();

            // pontos da esfera
            const cosY = Math.cos(this.rot), sinY = Math.sin(this.rot);
            const tilt = 0.35 + Math.sin(t * 0.3) * 0.1;
            const cosX = Math.cos(tilt), sinX = Math.sin(tilt);
            const amp = 0.035 + lv * 0.32 + (st === "thinking" ? 0.05 : 0);

            for (const p of this.points) {
                // distorção "orgânica" que cresce com o volume
                const d = 1 + amp * Math.sin(p.y * 5 + t * 2.3 + p.phase * 0.2) * Math.cos(p.x * 4 - t * 1.7);
                let x = p.x * d, y = p.y * d, z = p.z * d;
                const x1 = x * cosY - z * sinY;
                const z1 = x * sinY + z * cosY;
                const y1 = y * cosX - z1 * sinX;
                const z2 = y * sinX + z1 * cosX;
                const depth = (z2 + 1.4) / 2.4; // 0 (fundo) .. 1 (frente)
                const px = cx + x1 * R;
                const py = cy + y1 * R;
                const size = 0.7 + depth * 2.2;
                const a = 0.12 + depth * 0.85;
                ctx.fillStyle = `hsla(${hue}, ${sat}%, ${58 + depth * 24}%, ${a})`;
                ctx.fillRect(px - size / 2, py - size / 2, size, size);
            }

            // anéis orbitais
            ctx.lineWidth = 1.2;
            for (let i = 0; i < 3; i++) {
                const ang = this.rot * (i % 2 ? -1.3 : 0.9) + i * 1.05;
                ctx.strokeStyle = `hsla(${hue}, ${sat}%, 75%, ${0.18 + lv * 0.4})`;
                ctx.beginPath();
                ctx.ellipse(cx, cy, R * (1.18 + i * 0.07), R * (0.32 + i * 0.1), ang, 0, Math.PI * 2);
                ctx.stroke();
            }

            // moldura externa com marcações (estilo HUD)
            ctx.globalCompositeOperation = "source-over";
            const outer = R * 1.75;
            ctx.strokeStyle = `hsla(${hue}, ${sat}%, 70%, 0.22)`;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.arc(cx, cy, outer, 0, Math.PI * 2);
            ctx.stroke();

            ctx.strokeStyle = `hsla(${hue}, ${sat}%, 78%, 0.45)`;
            for (let i = 0; i < 72; i++) {
                const a = (i / 72) * Math.PI * 2 + t * 0.05;
                const len = i % 6 === 0 ? 10 : 4;
                ctx.beginPath();
                ctx.moveTo(cx + Math.cos(a) * outer, cy + Math.sin(a) * outer);
                ctx.lineTo(cx + Math.cos(a) * (outer - len), cy + Math.sin(a) * (outer - len));
                ctx.stroke();
            }

            // arcos que giram: rápidos quando pensando, verdes quando ouvindo
            const arcR = R * 1.5;
            const spin = st === "thinking" ? t * 3.2 : t * 0.4;
            ctx.lineWidth = st === "listening" ? 3 : 2;
            ctx.strokeStyle = `hsla(${hue}, ${sat}%, 80%, ${st === "idle" ? 0.35 : 0.75})`;
            for (let i = 0; i < 3; i++) {
                const start = spin + i * (Math.PI * 2 / 3);
                ctx.beginPath();
                ctx.arc(cx, cy, arcR, start, start + 0.7 + lv * 0.8);
                ctx.stroke();
            }

            requestAnimationFrame((tt) => this.frame(tt));
        },
    };

    // ------------------------------------------------------------ teclado e mouse

    let pttDown = false;
    const typing = () => !$("typeBox").hidden;

    function ptt(down) {
        if (down === pttDown || !document.body.classList.contains("ready")) return;
        if (down && Confirm.isOpen) return;
        pttDown = down;
        send({ type: "ptt", down });
    }

    addEventListener("keydown", (e) => {
        if (Confirm.isOpen) { // durante a confirmação, só Enter/S ou Esc/N
            e.preventDefault();
            if (e.key === "Enter" || e.key === "s" || e.key === "S") Confirm.answer(true);
            else if (e.key === "Escape" || e.key === "n" || e.key === "N") Confirm.answer(false);
            return;
        }

        if (typing()) {
            if (e.key === "Escape") hideType();
            return;
        }
        if (e.code === "Space") { e.preventDefault(); if (!e.repeat) ptt(true); }
        else if (e.key === "Enter") { e.preventDefault(); showType(); }
        else if (e.key === "Escape") send({ type: "minimize" });
        else if (e.ctrlKey && (e.key === "q" || e.key === "Q")) send({ type: "exit" });
    });
    addEventListener("keyup", (e) => {
        if (e.code === "Space" && !typing()) { e.preventDefault(); ptt(false); }
    });
    addEventListener("blur", () => ptt(false)); // evita o microfone "preso" ao trocar de janela

    // segurar o clique na esfera também funciona
    $("orb").addEventListener("pointerdown", (e) => {
        const dx = e.clientX - innerWidth / 2, dy = e.clientY - innerHeight * 0.44;
        if (Math.hypot(dx, dy) < Math.min(innerWidth, innerHeight) * 0.25) ptt(true);
    });
    addEventListener("pointerup", () => ptt(false));

    function showType() {
        if (!document.body.classList.contains("ready")) return;
        $("typeBox").hidden = false;
        $("typeInput").value = "";
        $("typeInput").focus();
    }
    function hideType() {
        $("typeBox").hidden = true;
        $("typeInput").blur();
    }
    $("typeInput").addEventListener("keydown", (e) => {
        if (e.key === "Enter") {
            e.preventDefault();
            const text = $("typeInput").value.trim();
            hideType();
            if (text) send({ type: "text", text });
        }
    });

    // ------------------------------------------------------------ modo demonstração (fora do app)

    const Demo = {
        onSend(msg) {
            if (msg.type === "ready") this.boot();
            if (msg.type === "ptt" && msg.down) { setState("listening"); this.fakeLevel(true); }
            if (msg.type === "ptt" && !msg.down) this.reply("Que horas são?");
            if (msg.type === "text") this.reply(msg.text);
        },
        async boot() {
            handle({ type: "config", userTitle: "senhor", city: "São Paulo" });
            const steps = [
                ["init", "Inicializando J.A.R.V.I.S."], ["cfg", "Carregando configurações"],
                ["core", "Núcleo OpenJarvis online"], ["model", "Modelo carregado: qwen3.5:4b"],
                ["stt", "Módulo de audição ativo (faster-whisper)"], ["tts", "Módulo de voz OpenJarvis ativo"],
                ["mic", "Calibrando microfone"],
            ];
            for (const [id, text] of steps) {
                handle({ type: "boot", id, text, status: "wait" });
                await sleep(280);
                handle({ type: "boot", id, text, status: "ok" });
            }
            handle({ type: "status", online: true, model: "qwen3.5:4b", listen: true, voice: "OpenJarvis" });
            handle({ type: "bootDone" });
            setState("idle");
            await sleep(1200);
            this.say("Boa tarde, senhor. Todos os sistemas operacionais. Segure espaço quando quiser falar comigo.");
        },
        async reply(text) {
            this.fakeLevel(false);
            handle({ type: "caption", role: "user", text });
            setState("thinking");
            await sleep(1400);
            const now = new Date().toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
            this.say(`São ${now}, senhor. Este é o modo de demonstração: abra pelo aplicativo para falar comigo de verdade.`);
        },
        async say(text) {
            handle({ type: "caption", role: "jarvis", text });
            setState("speaking");
            this.fakeLevel(true);
            await sleep(Math.min(9000, 900 + text.length * 55));
            this.fakeLevel(false);
            setState("idle");
        },
        timer: null,
        fakeLevel(on) {
            clearInterval(this.timer);
            Orb.level = 0;
            if (on) this.timer = setInterval(() => { Orb.level = Math.random() * 0.8 * Math.abs(Math.sin(Date.now() / 180)); }, 60);
        },
    };
    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

    // ------------------------------------------------------------ início

    Orb.init();
    setInterval(tick, 1000);
    tick();
    setState("booting");
    $("bootLines").innerHTML = '<span class="cursor"></span>';
    Boot.lines.clear();
    const firstLine = () => { const c = $("bootLines").querySelector(".cursor"); if (c) c.remove(); };
    const origLine = Boot.line.bind(Boot);
    Boot.line = (...a) => { firstLine(); origLine(...a); };

    window.__hud = { handle, Orb }; // útil para depurar (F12 com "Debug": true)
    send({ type: "ready" });
})();