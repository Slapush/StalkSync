# Changements

## 0.4.0-beta.6 (pré-version de test)
- Télémétrie BeamNG lue sur un fil dédié, dès l'arrivée de chaque paquet : aucun paquet perdu,
  même à 400 paquets/s. Boucle principale plus réactive (5 ms).
- Nouvel outil séparé **StalkSyncDebug.exe** : enregistre une session et crée un rapport JSON sur le Bureau.
  StalkSync lui relaie en local la télémétrie et ses décisions (127.0.0.1:4446).

## 0.4.0-beta.5 (pré-version de test)
- BeamNG : le retour pleins phares → croisement ne saccade plus. Une télémétrie lente renvoyait encore
  « pleins phares » juste après les appuis, et StalkSync rappuyait à tort. L'état lu n'est maintenant
  retenu que s'il est stable et apparu après le dernier appui.
- Appuis enchaînés sur la touche de mode des feux plus rapprochés (150 ms).
- Fréquence de réception de la télémétrie affichée dans la carte *Jeux*.

## 0.4.0-beta.4 (pré-version de test)
- Option « Sauter les veilleuses » : la position veilleuses du commodo laisse les phares éteints,
  on passe directement d'éteint au croisement.

## 0.4.0-beta.3 (pré-version de test)
- Télémétrie BeamNG.drive (OutGauge) : l'état réel des clignotants et des pleins phares recale StalkSync,
  les voyants clignotent au rythme du jeu, et rien n'est envoyé quand les warnings sont allumés.
- Voyants de phares sur le tableau de bord : veilleuses, croisement, pleins phares.

## 0.4.0-beta.2 (pré-version de test)
- Fenêtre en largeur sur trois colonnes : elle tient sur un écran 1080p.
- Les boutons du commodo s'affichent en court (« Bouton 10 »), le périphérique une seule fois.

## 0.4.0-beta.1 (pré-version de test)
- Gestion des phares avec le commodo (bêta) : éteint, veilleuses, croisement, pleins phares et appel de phare.
- Préréglage Moza : boutons 1 (éteint), 2 (veilleuses), 3 (croisement), 4 (pleins phares), 5 (retour au neutre), 6 (appel).
- ETS2 / ATS : touches L (mode des feux), K (pleins phares), J (appel de phare).
- BeamNG.drive : touche N (éteint → croisement → pleins phares) et appel de phare sur K, à assigner à « Flash high beam ».

## 0.3.1
- Logo de StalkSync dans l'exécutable, la fenêtre et la zone de notification.
- Informations de produit dans les propriétés du fichier.

## 0.3.0
- Compatibilité multi-jeux : ETS2 / ATS, BeamNG.drive, Assetto Corsa (Custom Shaders Patch), Assetto Corsa Competizione.
- Ajout de n'importe quel autre jeu depuis ses fenêtres ouvertes, avec ses propres touches.
- Un état de clignotant par jeu : seul le jeu au premier plan reçoit des touches.
- Touches avec Ctrl / Alt / Maj et flèches.

## 0.2.0
- Nouvelle interface en thème sombre, avec voyants de clignotant animés.
- Retrait de la télémétrie : StalkSync retient lui-même l'état du clignotant.

## 0.1.2
- Type de commodo « un bouton par position » pour le commodo Moza (impulsion à gauche, au centre, à droite).

## 0.1.1
- La détection retient tous les boutons enfoncés pendant le geste.

## 0.1.0
- Première version.
