# Nodalis 1.2 — Inbox vers fiche de maintenance (#110)

## Depuis l'Inbox

Sélectionner **une seule capture active**, puis cliquer sur **Maintenance…** ou
utiliser le clic droit **Ajouter à une fiche de maintenance**. Les captures
déjà converties ou placées dans la corbeille ne sont pas modifiées.

Le formulaire permet de choisir **Application · Projet**, parmi les projets
disposant déjà de \`Fiche de Maintenance.md\` (y compris **Ultra-simple**),
puis de choisir :

- **Nouvel incident** : une entrée Incident dans l'historique Markdown.
- **Intervention** : sélection facultative d'un incident existant de la
  fiche, ou intervention autonome.
- **Nouvel incident + première intervention** : deux entrées structurées
  avec le lien Incident, en **une seule sauvegarde atomique**.

Le résumé est proposé à partir de la première ligne de la capture et
reste modifiable. La date est celle de la capture, sans appeler l'IA.
L'aperçu en lecture seule montre le texte d'origine, le projet cible,
le Markdown projeté et les noms / tailles des pièces jointes. Pour une
capture image, il montre également une vignette décodée en taille limitée,
sans modifier le fichier d'origine.

Une capture image ou fichier conserve son contenu, avec des copies
d'attachements dans \`Attachments/Inbox-<id>/\` sous le projet **uniquement
lorsqu'il y a des fichiers joints**. Chaque fichier est lié depuis la
fiche ; les copies identiques sont réutilisées lors d'une reprise,
et toute collision différente est refusée sans remplacement.

Aucune réunion, aucun jalon, aucun autre dossier métier ne sont créés.
La structure initiale d'un projet Ultra-simple reste inchangée.

## Provenance et source canonique

Les nouveaux blocs conservent le contrat de maintenance :

\`\`\`markdown
## Historique de maintenance

### Incident
- **Identifiant :** INC-…
- **Date :** 2026-10-17
- **Résumé :** Résumé saisi par l'utilisateur
- **Provenance Inbox :** <identifiant de capture>
- **Origine :** <origine, si connue>
- **Source Inbox :** [Capture originale](../../Inbox/Items/<identifiant>/capture.md)

#### Capture Inbox

<!-- nodalis-inbox-maintenance:<identifiant de capture> -->
> Texte intégral de la capture
>
> Ligne suivante

Pièces jointes conservées :
- [capture.png](Attachments/Inbox-<identifiant>/capture.png)
\`\`\`

Le texte issu de la capture est cité avec \`>\`, pour que ses titres Markdown
n'introduisent pas accidentellement de nouveaux incidents.
La provenance comprend l'identifiant Inbox et un **lien Markdown
relatif direct vers la capture d'origine** (`capture.md`). Le chemin est
calculé depuis le projet et encodé comme URL. L'origine saisie lors de
la capture est conservée si elle existe.

L'entrée Inbox **n'est ni supprimée ni archivée** ; son statut devient
« traité », avec le chemin de la fiche convertie.

## Reprise après échec et non-duplication

La conversion est conçue pour être **idempotente** :

1. Les choix de conversion sont enregistrés dans
   \`Inbox/Items/<id>/maintenance-conversion.json\` avant toute modification
   de la fiche. Après interruption, le formulaire reprend ces choix,
   sans permettre de changer la destination.
2. Le Markdown contient un marqueur stable
   \`<!-- nodalis-inbox-maintenance:<id> -->\`.
   Si l'écriture du document a réussi mais que la mise à jour de l'état
   Inbox a été interrompue, la reprise retrouve le marqueur et
   **n'ajoute aucun autre bloc**.
3. Un verrou \`conversion.lock\` est partagé avec la conversion Inbox
   classique : deux instances Nodalis ne peuvent pas simultanément
   convertir le même item par deux modes différents.
4. La fiche existante est sauvegardée via \`TextDocumentSession\` ;
   les modifications externes détectées pendant la transaction
   provoquent un refus de sauvegarde, non un écrasement.
5. Quand la fiche est ouverte dans l'éditeur Nodalis, les modifications
   sont vidées via l'autosave avant la conversion ; si un conflit ou des
   modifications non sauvées subsistent, l'action est bloquée.

Les valeurs d'identification du bloc sont stables entre les tentatives ;
la source Inbox reste récupérable sur tout échec.
Les écritures dans la fiche sont tout-ou-rien pour les deux nouveaux blocs.
Les pièces jointes déjà copiées avant un échec restent réutilisables
sans conflit si leur empreinte SHA-256 correspond.

## Tests et limitations

\`MaintenanceInboxSmokeTests\` vérifie :

- création d'un incident depuis une capture texte, et vérification du
  contenu original cité ;
- conversion d'une image en intervention liée à un incident existant,
  avec copie conforme de la pièce jointe et lien Markdown ;
- mode incident + intervention en deux blocs structurés en une écriture ;
- reprise simulée après une écriture réussie mais avant la mise à jour Inbox ;
- second appel idempotent et refus d'un changement de projet en reprise ;
- incident inexistant, source intacte après erreur ;
- absence de réunions, jalons et autres structures dans un projet Ultra-simple.

La recette visuelle reste nécessaire sous Windows (DPI, dark mode,
navigation au clavier, onglets ouverts, récupération après erreur).

Voir aussi [Historique de maintenance](maintenance-history-1.2-guide.md),
[Contrat de maintenance](maintenance-markdown-1.2.md) et
[Guide Inbox](inbox-1.1-guide.md).
