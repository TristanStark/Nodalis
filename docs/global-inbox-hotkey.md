# Inbox 1.1 — raccourci global Windows

Issue #100. Le raccourci par défaut est **Ctrl+Shift+I**.

## Comportement

- Nodalis doit être lancé, avec un workspace ouvert en mode écriture.
- Le raccourci fonctionne même si Nodalis est en arrière-plan ou minimisé ;
  il restaure la fenêtre et ouvre une petite boîte de capture volontaire.
- L'utilisateur saisit du texte ou du Markdown, puis choisit Enregistrer
  ou Annuler. Seule la confirmation écrit un élément Inbox persistant.
- Une capture à la fois ; Windows MOD_NOREPEAT empêche les répétitions
  tant que les touches restent enfoncées.
- Aucun service Windows, aucune lecture automatique du presse-papiers,
  aucune interception continue du clavier, aucune IA ou connexion réseau.
- Si Nodalis est fermé, Windows ne conserve pas le raccourci enregistré.
- La capture reste également accessible dans la Command Palette
  (« Capturer dans Inbox »). Les autres formats arrivent dans #101.

## Configuration

Dans Préférences > Interface > Raccourci global Inbox, saisir une combinaison
de **deux modificateurs ou plus** (Ctrl, Alt, Shift, Win) et une lettre,
un chiffre, F1-F12 ou Espace, par exemple Ctrl+Alt+I.

- Combinaisons déjà utilisées localement par Nodalis rejetées.
- **Ctrl+Alt+N** reste réservé aux notes rapides et n'est jamais réaffecté.
- Windows peut rejeter les combinaisons réservées par le système ou par
  une autre application ; l'erreur est affichée et journalisée.
- Un changement de combinaison réussie prend effet immédiatement ;
  si Windows refuse, l'ancien enregistrement reste disponible.
- Configuration dans les préférences locales, clé
  `shortcutOverrides["inbox.capture.global"]`, sans changement du
  schéma métier 1.0.

## Vérification Windows

Sur Windows 10 et 11, après démarrage normal, vérifier la boîte de capture
au premier plan, en arrière-plan et fenêtre minimisée ; valider Annuler,
Enregistrer, double appui, répétition prolongée, fermeture complète de Nodalis,
détection de collision et changement de raccourci.

Contrôler affichage sombre, mise à l'échelle 100/125/150 %, focus clavier,
fenêtre d'erreur et journal de diagnostic en cas d'échec d'enregistrement.

Le raccourci natif n'est pas exécutable dans les smoke tests non interactifs ;
le build CI ne remplace pas cette vérification interactive sous Windows.
