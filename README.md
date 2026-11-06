# SilverClean

Limpeza e otimização do Windows no estilo do CCleaner, com o visual SILVER.

## Módulos

| Módulo | O que faz |
|---|---|
| **Limpeza** | Temporários do usuário e do Windows (mais de 24 h), Lixeira, cache de miniaturas, relatórios de erro, dumps de memória, logs antigos, cache do Windows Update, Otimização de Entrega, Prefetch e cache de shaders (DirectX/AMD/NVIDIA). |
| **Navegadores** | Chrome, Edge, Brave, Opera, Opera GX e Firefox: cache, histórico e cookies (os favoritos nunca são apagados). Aparecem só os navegadores instalados. |
| **Inicialização** | Lista o que abre com o Windows (Registro e pasta Inicializar) e ativa/desativa pelo mesmo mecanismo do Gerenciador de Tarefas. Nada é apagado. |
| **Programas** | Lista os programas instalados (tamanho, versão, data) com pesquisa e abre o desinstalador oficial. |
| **Análise de disco** | Mostra o tamanho de cada pasta com navegação por clique duplo e os 200 maiores arquivos. |

### Segurança
- Sempre analisa primeiro e só apaga depois da sua confirmação.
- Arquivos em uso são pulados automaticamente.
- Não segue junções/links simbólicos e nunca limpa a raiz de um disco nem pastas críticas inteiras.
- Se um navegador estiver aberto, pergunta se pode fechá-lo; se não, pula os itens dele.
- Tudo o que é removido fica registrado em `%LocalAppData%\SilverClean\logs`.
- Não tem "limpador de registro" (risco alto, ganho praticamente nenhum).

## Como compilar pelo GitHub
1. Crie um repositório e envie o conteúdo desta pasta (inclusive a pasta `.github`).
2. Vá em **Actions** → **Build SilverClean**. Ele roda sozinho a cada push (ou clique em *Run workflow*).
3. Quando terminar, baixe o artefato **SilverClean** → `SilverClean.exe`.

O .exe é único e já inclui o .NET 8, então não precisa instalar nada. Ao abrir, o Windows pede permissão de administrador (necessária para limpar as pastas do sistema).

## Compilar localmente (opcional)
Com o .NET 8 SDK instalado:
```
dotnet publish SilverClean/SilverClean.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
