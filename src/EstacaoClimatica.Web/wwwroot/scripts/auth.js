'use strict';

(function () {
    const versaoSdk = '12.13.0';
    const configuracaoFirebase = {
        apiKey: "SUA_CHAVE_FIREBASE",
        authDomain: 'seu-projeto.firebaseapp.com',
        projectId: 'seu-projeto',
        appId: 'SEU_APP_ID_FIREBASE'
    };

    let autenticacao = null;
    let usuarioAtual = null;
    let acessoInterno = false;
    let inicializacao = null;
    let recursosAuth = null;
    let sessaoTeste = false;
    const observadores = [];

    function ehUsuarioInterno(usuario = usuarioAtual) {
        return usuario === usuarioAtual && acessoInterno;
    }

    async function atualizarUsuario(usuario) {
        usuarioAtual = usuario;
        acessoInterno = false;
        if (usuario) {
            try {
                const token = await usuario.getIdToken();
                const resposta = await fetch('/api/internal-access', {
                    headers: { Authorization: `Bearer ${token}` }
                });
                if (usuarioAtual !== usuario) return;
                acessoInterno = resposta.ok;
            } catch (erro) {
                console.error('[Estação Climática] Falha ao verificar acesso interno.', erro);
            }
            if (usuarioAtual !== usuario) return;
        }
        observadores.forEach(observador => observador(usuario));
    }

    async function inicializar() {
        if (inicializacao) return inicializacao;

        inicializacao = (async () => {
            const [{ initializeApp }, moduloAuth] = await Promise.all([
                import(`https://www.gstatic.com/firebasejs/${versaoSdk}/firebase-app.js`),
                import(`https://www.gstatic.com/firebasejs/${versaoSdk}/firebase-auth.js`)
            ]);
            const aplicacao = initializeApp(configuracaoFirebase, 'estacao-climatica-dotnet');
            recursosAuth = moduloAuth;
            autenticacao = moduloAuth.getAuth(aplicacao);
            moduloAuth.onAuthStateChanged(autenticacao, usuario => { void atualizarUsuario(usuario); });
            return autenticacao;
        })();

        return inicializacao;
    }

    async function entrarComGoogle() {
        await inicializar();
        const provedor = new recursosAuth.GoogleAuthProvider();
        provedor.setCustomParameters({ prompt: 'select_account' });
        return recursosAuth.signInWithPopup(autenticacao, provedor);
    }

    async function sair() {
        if (sessaoTeste) {
            usuarioAtual = null;
            acessoInterno = false;
            observadores.forEach(observador => observador(null));
            return;
        }
        await inicializar();
        return recursosAuth.signOut(autenticacao);
    }

    async function observarEstado(funcaoRetorno) {
        observadores.push(funcaoRetorno);
        const ambienteTeste = navigator.webdriver && /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
        const emailTeste = typeof window.__climateTestUserEmail === 'string'
            ? window.__climateTestUserEmail
            : window.__climateTestAuthorized ? 'internal-test@example.test' : null;
        if (ambienteTeste && emailTeste) {
            sessaoTeste = true;
            acessoInterno = window.__climateTestAuthorized === true;
            usuarioAtual = {
                email: emailTeste,
                getIdToken: async () => 'browser-test-token'
            };
            funcaoRetorno(usuarioAtual);
            return;
        }
        try {
            await inicializar();
        } catch (erro) {
            console.error('[Estação Climática] Falha ao inicializar autenticação.', erro);
            funcaoRetorno(null);
        }
    }

    async function obterTokenInterno() {
        if (!ehUsuarioInterno()) return null;
        return usuarioAtual.getIdToken();
    }

    async function fetchInterno(url, opcoes = {}) {
        const token = await obterTokenInterno();
        if (!token) throw new Error('Usuário interno não autorizado.');
        const cabecalhos = new Headers(opcoes.headers || {});
        cabecalhos.set('Authorization', `Bearer ${token}`);
        cabecalhos.set('X-Climate-Timezone', Intl.DateTimeFormat().resolvedOptions().timeZone);
        return fetch(url, { ...opcoes, headers: cabecalhos });
    }

    window.ClimateAuth = {
        entrarComGoogle,
        sair,
        observarEstado,
        obterUsuarioAtual: () => usuarioAtual,
        ehUsuarioInterno,
        obterTokenInterno,
        fetchInterno
    };
})();
