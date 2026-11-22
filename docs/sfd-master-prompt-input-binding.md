# Génération des cinq prompts maîtres SFD : entrée par emplacements

## Comportement attendu

L'IA externe doit recevoir **un seul prompt maître complet**, dans l'ordre exact de son modèle original :

1. Instructions, rôle, mission, frontières, formalisme et format de sortie inchangés.
2. Rubrique **Entrées à coller** à la fin.
3. Contenu professionnel réellement documenté injecté **dans chaque emplacement**, dans l'ordre du prompt.

Il n'y a plus de préambule `# Contexte de travail préparé par Nodalis`, plus de copie brute des en-têtes d'identité Nodalis (GUID, SHA, chemins techniques), plus de bloc original non renseigné ajouté après le contexte.

## Sources et portée

| Prompt | Entrées remplies |
| --- | --- |
| 01 Cartographie | Présentation racine + notes liées explicitement ; inventaire PM/ACT canonique s'il existe ; décisions supplémentaires documentées |
| 02 Détail ACT | `Présentation.md` racine ; **PM parent réel entier** avec les ACT qu'il contient ; ACT cible sélectionnée explicitement ; notes liées ; fiche ACT antérieure déjà rédigée si elle existe |
| 03 Assemblage FCT | Présentation racine ; tous PM/ACT documentés ; synthèses ACT écrites ; notes liées ; FCT précédemment créées |
| 04 Exigences | Présentation racine ; FCT entière et toutes ses ACT associées groupées par PM ; fiche ACT sélectionnée ; exigences candidates, RM et notes |
| 05 EMO | Présentation racine ; FCT et ACT associées ; exigences candidates de ses ACT ; RM connues ; mode MONOBLOC par défaut |

Les sources facultatives réellement absentes sont indiquées `Non fourni`, `Aucun` ou avec une réserve appropriée. **Nodalis n'invente ni périmètre, ni règles, ni faits métier**. Le stockage historique des exigences par ACT ne constitue pas une preuve d'une couverture FCT×ACT : la prévisualisation le signale.

Les sources sont des fichiers Markdown ou TXT déjà présents dans le workspace, dont la lecture est bornée et vérifiée. Aucun DOCX n'est converti ou importé. Aucun appel IA n'est lancé.

## Sélection et vigilance

Dans le parcours SFD, choisir **d'abord le processus**, puis son activité dans la liste filtrée. La sélection d'une ACT ou FCT n'est plus imposée par défaut. Le générateur refuse la préparation des prompts 02/04 sans ACT explicite ou des prompts 04/05 sans FCT, et vérifie le lien FCT×ACT pour le 04.

Les grandes sources obligatoires sont refusées si elles dépassent les bornes ; elles ne sont jamais tronquées silencieusement. Les avertissements restent visibles dans la fenêtre de préparation plutôt qu'à l'intérieur du prompt copié.

## Qualification

Les tests de régression couvrent l'ordre des consignes originales, le remplissage des cinq modèles, la non-exposition des métadonnées, l'interdiction DOCX et le choix de l'ACT exacte quand plusieurs PM coexistent. La validation visuelle et une relecture métier doivent encore confirmer que les sections de chaque source sont utiles à l'IA cible.
