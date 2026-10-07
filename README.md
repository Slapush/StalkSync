# StalkSync

Petit utilitaire Windows qui fait fonctionner le commodo de clignotants (ex. Moza Multi-function Stalks)
comme sur un vrai véhicule dans Euro Truck Simulator 2 et American Truck Simulator.

ETS2 traite le clignotant comme une bascule : un appui l'allume, un autre l'éteint. Un commodo qui reste
en position ne renvoie pas de second appui en revenant au centre, et le jeu finit désynchronisé.
StalkSync lit la position du commodo et l'état réel du camion, puis envoie la touche du jeu uniquement
quand les deux diffèrent.

> **État : non testé sur Windows ni avec le matériel Moza.** Le code compile et la logique est couverte
> par des tests unitaires, mais la lecture du commodo, la télémétrie et l'envoi de touches n'ont pas
> encore été essayés en jeu.

## Fonctions

- **Synchronisation** : commodo à gauche = clignotant gauche, au centre = éteint.
- **Clignotements confort** : un appui bref donne 3 clignotements (nombre et durée réglables).
- **Retour automatique** après le virage, basé sur l'angle du volant (commodo à impulsion).
- **Trois types de commodo** : maintenu (reste en position), impulsion (revient au centre), ou un bouton
  par position (Moza : une impulsion en arrivant à gauche, au centre ou à droite).
- **Aucune touche envoyée** si la fenêtre active n'est pas le jeu, si le jeu est en pause ou si les warnings sont allumés.

## Installation

1. Télécharger `StalkSync.exe` depuis la page *Releases* et le lancer (aucune installation).
2. Dans StalkSync, cliquer **Détecter** pour la gauche puis pousser le commodo à fond à gauche ; idem à droite.
3. Choisir le type de commodo. Pour le type **un bouton par position** (commodo Moza), mettre le commodo
   à fond d'un côté, cliquer **Détecter** sur la ligne *Centre* puis le ramener au centre.
4. Vérifier que les touches affichées sont celles des clignotants dans *Options → Touches* du jeu
   (par défaut `[` et `]`). Sinon, cliquer **Définir** et appuyer sur la bonne touche.
5. Dans le jeu, **retirer le commodo des réglages de clignotants** (sinon le jeu reçoit la commande deux fois).

Si le jeu est lancé en administrateur, StalkSync doit l'être aussi, sinon Windows bloque les touches.

## Télémétrie

Sans télémétrie, StalkSync fonctionne « à l'aveugle » : il suppose que chaque touche envoyée a été prise
en compte. La synchronisation garantie, les clignotements confort précis et le retour automatique
demandent la télémétrie.

Le jeu n'expose sa télémétrie qu'à travers un plugin du SDK officiel de SCS. StalkSync lit la mémoire
partagée du plugin [scs-sdk-plugin de RenCloud](https://github.com/RenCloud/scs-sdk-plugin) (révision 12) :

1. Télécharger `scs-telemetry.dll` (Win64) depuis les releases du plugin.
2. Le copier dans `<dossier du jeu>\bin\win_x64\plugins\` (créer le dossier `plugins` s'il n'existe pas).
3. Au lancement, le jeu affiche un avertissement sur les fonctions SDK : l'accepter.

C'est un fichier **ajouté**, aucun fichier du jeu n'est modifié ; il suffit de le supprimer pour revenir
en arrière. StalkSync ne lit cette mémoire qu'en lecture et n'injecte rien dans le jeu.

**TruckersMP** : ce plugin de télémétrie est celui qu'utilisent les outils de suivi de VTC courants ;
vérifier tout de même les règles en vigueur avant de jouer en ligne. StalkSync lui-même ne fait
qu'envoyer des appuis de touches, comme un clavier.

## Réglage conseillé dans le jeu

Avec un commodo **maintenu**, désactiver l'extinction automatique des clignotants dans les options du jeu :
sinon le jeu coupe le clignotant après le virage, et StalkSync le rallume tant que le commodo est enclenché.

## Développement

```
dotnet test tests/StalkSync.Tests
dotnet publish src/StalkSync -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

- `src/StalkSync.Core` : logique de décision, sans dépendance Windows, testée.
- `src/StalkSync` : application WinForms (.NET 8) — DirectInput, mémoire partagée, `SendInput`.
- Réglages enregistrés dans `%AppData%\StalkSync\config.json`.
