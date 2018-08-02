# WindowsService + ConsoleApplicationInstaller

O exemplo atual conta com um Serviço de Windows com um Timer que executa de tempo em tempo e uma aplicação em console responsável pela instalação.

Os parametros de configuração estão no arquivo App.config

## O instalador
Após várias tentativas frustradas de utilizar o MSI Installer (VisualStudio SetupProject) e também o WiXInstaller, optei por desenvolver um ConsoleApplication para realizar corretamente os procedimentos.

O instalador via Console Application tem opção de instalar e de desinstalar o serviço.

Ao compilar a solução em modo RELEASE, é gerado automaticamente a pasta Setup contendo os arquivos necessários para a instalação e distribuição.

## Para distribuir
Basta zipar a pasta Setup, enviar para o destino e orientar para abrir o programa Setup.exe contido na pasta.
