<p align="center"><img src="docs/logo.png" width="128" alt="StalkSync"></p>

<h1 align="center">StalkSync</h1>

<p align="center">
  <a href="https://github.com/Slapush/StalkSync/releases"><img src="https://img.shields.io/github/v/release/Slapush/StalkSync?include_prereleases&label=version" alt="Version"></a>
  <a href="https://github.com/Slapush/StalkSync/actions/workflows/build.yml"><img src="https://github.com/Slapush/StalkSync/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/licence-MIT-green" alt="Licence MIT"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-blue" alt="Windows 10 / 11">
</p>

<p align="center"><b><a href="https://github.com/Slapush/StalkSync/releases/latest">⬇ Télécharger StalkSync.exe</a></b></p>

Petit utilitaire Windows qui fait fonctionner le commodo de clignotants (ex. Moza Multi-function Stalks)
comme sur un vrai véhicule dans Euro Truck Simulator 2, American Truck Simulator, BeamNG.drive,
Assetto Corsa, Assetto Corsa Competizione… et n'importe quel jeu qui allume ses clignotants avec une touche.

Ces jeux traitent le clignotant comme une bascule : un appui l'allume, un autre l'éteint. Un commodo qui reste
en position ne renvoie pas de second appui en revenant au centre, et le jeu finit désynchronisé.
StalkSync suit la position du commodo, retient l'état du clignotant dans le jeu et n'envoie la touche
du jeu que quand les deux diffèrent.

Testé avec le commodo Moza Multi-function Stalks dans ETS2.

## Fonctions

- **Synchronisation** : commodo à gauche = clignotant gauche, au centre = éteint.
- **Clignotements confort** : un appui bref donne 3 clignotements (nombre et durée réglables).
- **Trois types de commodo** : maintenu (reste en position), impulsion (revient au centre), ou un bouton
  par position (Moza : une impulsion en arrivant à gauche, au centre ou à droite).
- **Phares (bêta)** : éteint, veilleuses, croisement, pleins phares et appel de phare maintenu tant que le
  commodo est tiré. Préréglage pour le commodo Moza (boutons 1 à 6).
- **Plusieurs jeux** : chacun a ses touches, et StalkSync reconnaît le jeu au premier plan.
- **Aucune touche envoyée** si la fenêtre active n'est pas un jeu de la liste : StalkSync attend que le jeu repasse au premier plan.

## Jeux

| Jeu | Touches par défaut | Remarque |
|---|---|---|
| Euro Truck Simulator 2 / American Truck Simulator | `[` et `]` | Désactiver l'extinction automatique des clignotants. |
| BeamNG.drive | `,` et `.` | |
| Assetto Corsa | à définir | Clignotants ajoutés par Custom Shaders Patch : leur assigner une touche dans les commandes. |
| Assetto Corsa Competizione | `Alt + ←` et `Alt + →` | |

Phares (bêta) : ETS2 / ATS utilisent `L` (mode des feux), `K` (pleins phares) et `J` (appel de phare).
BeamNG.drive utilise `N` (éteint → croisement → pleins phares, sans veilleuses) ; l'appel de phare n'y a
pas de touche par défaut : assigner `K` à « Flash high beam » dans Options → Contrôles.

Pour un autre jeu : lancer le jeu, cliquer **Ajouter…** dans la carte *Jeux*, choisir sa fenêtre, puis
définir les touches de clignotant réglées dans ce jeu.

## Télémétrie BeamNG.drive

Facultatif, mais conseillé : avec la télémétrie, StalkSync connaît l'état réel des clignotants et des pleins
phares dans BeamNG et se recale tout seul si le jeu change d'état sans lui (extinction automatique d'un
clignotant, touche du clavier…). Les voyants de StalkSync clignotent alors au rythme du jeu.

Dans BeamNG.drive : **Options → Autres → Protocoles → OutGauge**, l'activer avec l'adresse `127.0.0.1` et le
port `4444`. La carte *Jeux* de StalkSync indique quand la télémétrie est reçue. StalkSync ne fait que lire
ces données ; rien n'est envoyé au jeu.

## Installation

1. Télécharger `StalkSync.exe` depuis la page [Releases](https://github.com/Slapush/StalkSync/releases)
   et le lancer : aucune installation, aucun .NET à installer (Windows 10 ou 11, 64 bits).
   Windows SmartScreen peut avertir au premier lancement, l'exécutable n'étant pas signé :
   *Informations complémentaires → Exécuter quand même*.
2. Dans StalkSync, cliquer **Détecter** pour la gauche puis pousser le commodo à fond à gauche ; idem à droite.
3. Choisir le type de commodo. Pour le type **un bouton par position** (commodo Moza), mettre le commodo
   à fond d'un côté, cliquer **Détecter** sur la ligne *Centre* puis le ramener au centre.
4. Dans la carte *Jeux*, choisir son jeu et vérifier que les touches affichées sont celles des
   clignotants dans les options du jeu. Sinon, cliquer **Définir** et appuyer sur la bonne touche
   (Ctrl, Alt et Maj sont acceptés).
5. Dans le jeu, **retirer le commodo des réglages de clignotants** (sinon le jeu reçoit la commande deux fois).

Si le jeu est lancé en administrateur, StalkSync doit l'être aussi, sinon Windows bloque les touches.

## Réglage conseillé dans le jeu

Désactiver l'extinction automatique des clignotants si le jeu en a une. StalkSync ne voit pas
ce que fait le jeu : si le jeu coupe le clignotant tout seul, StalkSync le croit encore allumé et
appuiera à contretemps au retour du commodo au centre.

## Jeu en ligne

StalkSync ne fait qu'envoyer des appuis de touches, comme un clavier : il ne lit ni ne modifie
la mémoire du jeu et n'injecte rien. Vérifier tout de même les règles du serveur ou de la
plateforme en ligne utilisée (TruckersMP, serveurs Assetto Corsa…).

## Signaler un problème

Ouvrir une [issue](https://github.com/Slapush/StalkSync/issues/new/choose) en précisant le jeu,
le modèle de commodo, le type choisi dans StalkSync et, si possible, une capture de la fenêtre.

## Développement

Prérequis : [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0). La solution `StalkSync.sln`
s'ouvre aussi dans Visual Studio ou Rider.

```
dotnet test tests/StalkSync.Tests
dotnet publish src/StalkSync -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

- `src/StalkSync.Core` : logique de décision, sans dépendance Windows, testée.
- `src/StalkSync` : application WinForms (.NET 8) — DirectInput, `SendInput`.
- Réglages enregistrés dans `%AppData%\StalkSync\config.json`.

## Licence

[MIT](LICENSE) © 2026 Slapush. Composants tiers : voir [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

StalkSync n'est affilié ni à SCS Software, BeamNG GmbH, Kunos Simulazioni ou MOZA Racing.
Les noms de jeux et de matériel cités appartiennent à leurs propriétaires respectifs.

---

**English** — StalkSync makes a turn-signal stalk (e.g. Moza Multi-function Stalks) behave like a real
car's in ETS2, ATS, BeamNG.drive, Assetto Corsa, ACC and any game that toggles its blinkers with a key:
it tracks the stalk position and only presses the game's blinker key when the game is out of sync.
Download the exe from [Releases](https://github.com/Slapush/StalkSync/releases); the interface is in French.
