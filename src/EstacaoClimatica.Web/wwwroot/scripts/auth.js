'use strict';

(function () {
    const versaoSdk = '12.13.0';
    const usuariosInternos = ['usuario@example.com', 'outra@example.com'];
    const configuracaoFirebase = {
        apiKey: 'SUA_CHAVE_FIREBASE',
        authDomain: 'seu-projeto.firebaseapp.com',
        projectId: 'seu-projeto',
        appId: 'SEU_APP_ID_FIREBASE'
    };

    let autenticacao = null;
    let usuarioAtual = null;
    let inicializacao = null;
    let recursosAuth = null;
    let sessaoTeste = false;
    const observadores = [];

    function normalizarEmail(email) {
        return String(email || '').trim().toLowerCase();
    }

    function ehUsuarioInterno(usuario = usuarioAtual) {
        return usuariosInternos.includes(normalizarEmail(usuario?.email));
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
            moduloAuth.onAuthStateChanged(autenticacao, usuario => {
                usuarioAtual = usuario;
                observadores.forEach(observador => observador(usuario));
            });
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
            : window.__climateTestAuthorized ? 'usuario@example.com' : null;
        if (ambienteTeste && emailTeste) {
            sessaoTeste = true;
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
